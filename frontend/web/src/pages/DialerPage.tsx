import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Device, type Call } from "@twilio/voice-sdk";
import {
  Grid3x3,
  History,
  Contact,
  X,
  Voicemail,
  Phone,
  PhoneOff,
  Mic,
  MicOff,
  ChevronDown,
  User,
  Settings as SettingsIcon,
  Trash2,
} from "lucide-react";
import { api } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { LogoutConfirmModal } from "../components/LogoutConfirmModal";

type Screen = "keypad" | "recents" | "contacts" | "settings";
type CallState = "idle" | "connecting" | "in-call" | "ended";
type PresenceStatus = "Online" | "Away" | "DND";

const STATUS_COLORS: Record<PresenceStatus, string> = {
  Online: "#2ecc71",
  Away: "#f0a83f",
  DND: "#e74c3c",
};

interface RecentCall {
  id: string;
  destinationNumber: string;
  direction: string;
  status: string;
  startedAt: string | null;
  durationSeconds: number;
}

interface PhoneContact {
  id: string;
  firstName: string | null;
  lastName: string | null;
  phoneNumber: string;
  email: string | null;
  notes: string | null;
}

const KEYS: [string, string][] = [
  ["1", ""],
  ["2", "ABC"],
  ["3", "DEF"],
  ["4", "GHI"],
  ["5", "JKL"],
  ["6", "MNO"],
  ["7", "PQRS"],
  ["8", "TUV"],
  ["9", "WXYZ"],
  ["*", ""],
  ["0", "+"],
  ["#", ""],
];

function formatDuration(total: number) {
  const m = Math.floor(total / 60).toString().padStart(2, "0");
  const s = (total % 60).toString().padStart(2, "0");
  return `${m}:${s}`;
}

function formatWhen(iso: string | null) {
  if (!iso) return "";
  const date = new Date(iso);
  const isToday = date.toDateString() === new Date().toDateString();
  const time = date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
  return isToday ? `Today ${time}` : `${date.toLocaleDateString()} ${time}`;
}

function loadSoundPref(key: string): boolean {
  return localStorage.getItem(key) !== "0";
}

// DTMF frequency pairs (ITU-T Q.23) — a short, real dial-pad tone for local
// keypress feedback, independent of the DTMF signal actually sent to the
// far end via Call.sendDigits.
const DTMF_TONES: Record<string, [number, number]> = {
  "1": [697, 1209], "2": [697, 1336], "3": [697, 1477],
  "4": [770, 1209], "5": [770, 1336], "6": [770, 1477],
  "7": [852, 1209], "8": [852, 1336], "9": [852, 1477],
  "*": [941, 1209], "0": [941, 1336], "#": [941, 1477],
};

let dialToneAudioContext: AudioContext | null = null;

function playDialTone(digit: string) {
  const freqs = DTMF_TONES[digit];
  if (!freqs) return;
  dialToneAudioContext ??= new AudioContext();
  const ctx = dialToneAudioContext;
  const now = ctx.currentTime;
  const gain = ctx.createGain();
  gain.gain.setValueAtTime(0.08, now);
  gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.12);
  gain.connect(ctx.destination);
  for (const freq of freqs) {
    const osc = ctx.createOscillator();
    osc.type = "sine";
    osc.frequency.value = freq;
    osc.connect(gain);
    osc.start(now);
    osc.stop(now + 0.12);
  }
}

export function DialerPage() {
  const { token, claims, logout } = useAuth();
  const navigate = useNavigate();
  const deviceRef = useRef<Device | null>(null);
  const callRef = useRef<Call | null>(null);
  const timerRef = useRef<number | null>(null);

  const [screen, setScreen] = useState<Screen>("keypad");
  const [ready, setReady] = useState(false);
  const [number, setNumber] = useState("");
  const [callState, setCallState] = useState<CallState>("idle");
  const [muted, setMuted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [seconds, setSeconds] = useState(0);
  const [autoAnswer, setAutoAnswer] = useState(false);
  const [dnd, setDnd] = useState(false);
  const [status, setStatus] = useState<PresenceStatus>("Online");
  const [statusOpen, setStatusOpen] = useState(false);
  const [logoutConfirm, setLogoutConfirm] = useState(false);
  const [photoUrl, setPhotoUrl] = useState<string | null>(null);

  const [recents, setRecents] = useState<RecentCall[] | null>(null);
  const [recentsError, setRecentsError] = useState<string | null>(null);

  const [contacts, setContacts] = useState<PhoneContact[] | null>(null);
  const [contactsError, setContactsError] = useState<string | null>(null);
  const [newContactName, setNewContactName] = useState("");
  const [newContactNumber, setNewContactNumber] = useState("");

  const [inputDevices, setInputDevices] = useState<MediaDeviceInfo[]>([]);
  const [outputDevices, setOutputDevices] = useState<MediaDeviceInfo[]>([]);
  const [selectedInput, setSelectedInput] = useState("");
  const [selectedOutput, setSelectedOutput] = useState("");

  const [ringtoneEnabled, setRingtoneEnabled] = useState(() => loadSoundPref("voxlink_sound_ringtone"));
  const [alertEnabled, setAlertEnabled] = useState(() => loadSoundPref("voxlink_sound_alert"));
  const [dialPadToneEnabled, setDialPadToneEnabled] = useState(() => loadSoundPref("voxlink_sound_dialpad"));

  function refreshDeviceLists(device: Device) {
    if (!device.audio) return;
    setInputDevices(Array.from(device.audio.availableInputDevices.values()));
    setOutputDevices(Array.from(device.audio.availableOutputDevices.values()));
  }

  function toggleRingtone(value: boolean) {
    setRingtoneEnabled(value);
    localStorage.setItem("voxlink_sound_ringtone", value ? "1" : "0");
    deviceRef.current?.audio?.incoming(value);
  }

  function toggleAlert(value: boolean) {
    setAlertEnabled(value);
    localStorage.setItem("voxlink_sound_alert", value ? "1" : "0");
    deviceRef.current?.audio?.disconnect(value);
  }

  function toggleDialPadTone(value: boolean) {
    setDialPadToneEnabled(value);
    localStorage.setItem("voxlink_sound_dialpad", value ? "1" : "0");
  }

  useEffect(() => {
    let cancelled = false;

    async function setup() {
      try {
        const { token: voiceToken } = await api.get<{ token: string }>("/api/calls/voice-token", token);
        if (cancelled) return;

        const device = new Device(voiceToken);
        device.on("registered", () => setReady(true));
        device.on("unregistered", () => setReady(false));
        device.on("error", (err) => setError(err.message));
        device.on("tokenWillExpire", async () => {
          const refreshed = await api.get<{ token: string }>("/api/calls/voice-token", token);
          device.updateToken(refreshed.token);
        });

        await device.register();
        deviceRef.current = device;
        refreshDeviceLists(device);
        device.audio?.on("deviceChange", () => refreshDeviceLists(device));
        device.audio?.incoming(loadSoundPref("voxlink_sound_ringtone"));
        device.audio?.disconnect(loadSoundPref("voxlink_sound_alert"));
      } catch (err) {
        console.error("Softphone setup failed:", err);
        const detail = err instanceof Error ? err.message : String(err);
        setError(`Could not set up the softphone: ${detail}`);
      }
    }

    setup();
    return () => {
      cancelled = true;
      deviceRef.current?.destroy();
      deviceRef.current = null;
    };
  }, [token]);

  useEffect(() => {
    api
      .get<{ photoUrl: string | null }>("/api/users/me", token)
      .then((p) => setPhotoUrl(p.photoUrl))
      .catch(() => {});
  }, [token]);

  useEffect(() => {
    if (callState === "in-call") {
      timerRef.current = window.setInterval(() => setSeconds((s) => s + 1), 1000);
    } else {
      if (timerRef.current) window.clearInterval(timerRef.current);
      setSeconds(0);
    }
    return () => {
      if (timerRef.current) window.clearInterval(timerRef.current);
    };
  }, [callState]);

  function loadRecents() {
    api
      .get<RecentCall[]>("/api/calls/recent", token)
      .then((data) => {
        setRecents(data);
        setRecentsError(null);
      })
      .catch((err) => setRecentsError(err instanceof Error ? err.message : "Failed to load call history."));
  }

  function loadContacts() {
    api
      .get<PhoneContact[]>("/api/contacts", token)
      .then((data) => {
        setContacts(data);
        setContactsError(null);
      })
      .catch((err) => setContactsError(err instanceof Error ? err.message : "Failed to load contacts."));
  }

  async function addContact() {
    if (!newContactNumber.trim()) return;
    const [firstName, ...rest] = newContactName.trim().split(" ");
    try {
      await api.post(
        "/api/contacts",
        { firstName: firstName || null, lastName: rest.join(" ") || null, phoneNumber: newContactNumber.trim() },
        token,
      );
      setNewContactName("");
      setNewContactNumber("");
      loadContacts();
    } catch (err) {
      setContactsError(err instanceof Error ? err.message : "Failed to add contact.");
    }
  }

  async function deleteContact(id: string) {
    try {
      await api.delete(`/api/contacts/${id}`, token);
      setContacts((current) => (current ? current.filter((c) => c.id !== id) : current));
    } catch (err) {
      setContactsError(err instanceof Error ? err.message : "Failed to delete contact.");
    }
  }

  async function deleteRecent(id: string) {
    try {
      await api.delete(`/api/calls/${id}`, token);
      setRecents((current) => (current ? current.filter((r) => r.id !== id) : current));
    } catch (err) {
      setRecentsError(err instanceof Error ? err.message : "Failed to delete call.");
    }
  }

  function goTo(next: Screen) {
    setScreen(next);
    if (next === "recents") loadRecents();
    if (next === "contacts") loadContacts();
  }

  function resetAfterCall() {
    callRef.current = null;
    setMuted(false);
    setCallState((current) => {
      if (current === "in-call" || current === "connecting") {
        window.setTimeout(() => setCallState("idle"), 1200);
        return "ended";
      }
      return "idle";
    });
    if (screen === "recents") loadRecents();
  }

  function pressDigit(digit: string) {
    if (dialPadToneEnabled) playDialTone(digit);
    if (callState === "in-call" && callRef.current) {
      callRef.current.sendDigits(digit);
    } else if (callState === "idle") {
      setNumber((n) => n + digit);
    }
  }

  async function placeCall(dialNumber?: string) {
    const target = dialNumber ?? number;
    if (!deviceRef.current || !target) return;
    setError(null);
    setNumber(target);
    setCallState("connecting");
    try {
      const call = await deviceRef.current.connect({ params: { To: target } });
      callRef.current = call;
      call.on("accept", () => setCallState("in-call"));
      call.on("disconnect", resetAfterCall);
      call.on("cancel", resetAfterCall);
      call.on("reject", resetAfterCall);
      call.on("error", (err) => {
        setError(err.message);
        resetAfterCall();
      });
    } catch {
      setError("Could not place the call.");
      setCallState("idle");
    }
  }

  function hangUp() {
    callRef.current?.disconnect();
  }

  function toggleMute() {
    if (!callRef.current) return;
    const next = !muted;
    callRef.current.mute(next);
    setMuted(next);
  }

  function redial(destination: string) {
    setNumber(destination);
    setScreen("keypad");
  }

  async function changeInputDevice(deviceId: string) {
    setSelectedInput(deviceId);
    await deviceRef.current?.audio?.setInputDevice(deviceId);
  }

  async function changeOutputDevice(deviceId: string) {
    setSelectedOutput(deviceId);
    await deviceRef.current?.audio?.speakerDevices.set(deviceId);
  }

  const inCall = callState === "in-call" || callState === "connecting" || callState === "ended";
  const displayNumber = number || "";

  return (
    <div className="softphone">
      <div className="softphone-topstrip">
        <ToggleSwitch label="Auto Answer" checked={autoAnswer} onChange={setAutoAnswer} />
        <ToggleSwitch label="DND" checked={dnd} onChange={setDnd} />

        <div className="softphone-identity-wrap">
          <div className="softphone-identity" onClick={() => setStatusOpen((v) => !v)}>
            <ChevronDown size={14} className="softphone-muted-icon" />
            <div className="softphone-identity-text">
              <div className="softphone-identity-name">{claims?.email ?? "You"}</div>
              <div className="softphone-identity-sub">{ready ? "REGISTERED" : "CONNECTING…"}</div>
            </div>
            <div
              className="softphone-avatar-sm"
              style={photoUrl ? { backgroundImage: `url(${photoUrl})`, backgroundSize: "cover", backgroundPosition: "center" } : undefined}
            >
              {!photoUrl && <User size={16} />}
              <span className="softphone-status-dot" style={{ background: STATUS_COLORS[status] }} />
            </div>
          </div>

          {statusOpen && (
            <div className="softphone-status-menu">
              {(["Online", "Away", "DND"] as PresenceStatus[]).map((s) => (
                <div
                  key={s}
                  className="softphone-status-menu-item"
                  onClick={() => {
                    setStatus(s);
                    setStatusOpen(false);
                  }}
                >
                  <span className="softphone-status-dot-inline" style={{ background: STATUS_COLORS[s] }} />
                  {s}
                  {status === s && <span className="softphone-status-check">✓</span>}
                </div>
              ))}
              <div className="softphone-status-menu-divider" />
              <div
                className="softphone-status-menu-item softphone-status-menu-logout"
                onClick={() => {
                  setStatusOpen(false);
                  setLogoutConfirm(true);
                }}
              >
                Logout
              </div>
            </div>
          )}
        </div>
      </div>

      {error && <div className="error">{error}</div>}

      {logoutConfirm && (
        <LogoutConfirmModal
          onCancel={() => setLogoutConfirm(false)}
          onConfirm={() => {
            logout();
            navigate("/login");
          }}
        />
      )}

      <div className="softphone-body">
        <div className="softphone-sidebar">
          <SidebarItem icon={<Grid3x3 size={20} />} label="Keypad" active={screen === "keypad"} onClick={() => goTo("keypad")} />
          <SidebarItem icon={<History size={20} />} label="Recents" active={screen === "recents"} onClick={() => goTo("recents")} />
          <SidebarItem icon={<Contact size={20} />} label="Contacts" active={screen === "contacts"} onClick={() => goTo("contacts")} />
          <div className="softphone-sidebar-spacer" />
          <SidebarItem icon={<SettingsIcon size={20} />} label="Settings" active={screen === "settings"} onClick={() => goTo("settings")} />
        </div>

        <div className="softphone-main">
          {screen === "keypad" && (
            <div className="softphone-keypad-screen">
              <div className="softphone-dialer-col">
                <div className="softphone-dialer-header">Active Calls</div>

                <div className="softphone-dialer-card">
                  <div className="softphone-caller-id">Caller ID: {claims?.email ?? "you"}</div>

                  {callState === "idle" ? (
                    <div className="softphone-number-row">
                      <input
                        className="softphone-number-input"
                        placeholder="Enter a number"
                        value={number}
                        onChange={(e) => setNumber(e.target.value.replace(/[^\d+*#]/g, ""))}
                      />
                      {number && (
                        <X size={16} className="softphone-clear-icon" onClick={() => setNumber("")} />
                      )}
                    </div>
                  ) : (
                    <div className="softphone-number-row softphone-number-row-static">{displayNumber}</div>
                  )}

                  <div className="dialpad">
                    {KEYS.map(([digit, letters]) => (
                      <button
                        key={digit}
                        type="button"
                        className="dialpad-key"
                        onClick={() => pressDigit(digit)}
                        disabled={callState === "connecting" || callState === "ended"}
                      >
                        <div>{digit}</div>
                        {letters && <div className="dialpad-key-sub">{letters}</div>}
                      </button>
                    ))}
                  </div>

                  <div className="softphone-action-row">
                    {!inCall ? (
                      <>
                        <div className="softphone-round-btn softphone-round-btn-muted" title="Voicemail">
                          <Voicemail size={16} />
                        </div>
                        <button
                          type="button"
                          className="softphone-round-btn softphone-round-btn-call"
                          disabled={!ready || !number}
                          onClick={() => placeCall()}
                          title="Call"
                        >
                          <Phone size={22} />
                        </button>
                        <div className="softphone-pill">DTMF</div>
                      </>
                    ) : (
                      <>
                        <button
                          type="button"
                          className={muted ? "softphone-round-btn softphone-round-btn-active" : "softphone-round-btn softphone-round-btn-muted"}
                          onClick={toggleMute}
                          disabled={callState !== "in-call"}
                          title={muted ? "Unmute" : "Mute"}
                        >
                          {muted ? <MicOff size={16} /> : <Mic size={16} />}
                        </button>
                        <button
                          type="button"
                          className="softphone-round-btn softphone-round-btn-hangup"
                          onClick={hangUp}
                          disabled={callState !== "in-call"}
                          title="Hang up"
                        >
                          <PhoneOff size={22} />
                        </button>
                        <div className="softphone-pill">DTMF</div>
                      </>
                    )}
                  </div>
                </div>
              </div>

              <div className="softphone-stage">
                <div className="softphone-stage-tabs">
                  <span className="softphone-tab-muted">Favorites</span>
                  <span className="softphone-tab-muted" onClick={() => goTo("recents")} style={{ cursor: "pointer", color: "var(--text)" }}>
                    Recent
                  </span>
                  <span className="softphone-tab-muted">Missed Call</span>
                </div>

                <div className="softphone-stage-center">
                  <div className="softphone-avatar">
                    <Contact size={56} />
                  </div>
                  <div className="softphone-stage-number">{displayNumber || "—"}</div>
                  {inCall ? (
                    <div className="softphone-stage-sub">
                      {callState === "connecting" ? "Calling…" : callState === "ended" ? "Call ended" : formatDuration(seconds)}
                    </div>
                  ) : (
                    <div className="softphone-stage-sub">{displayNumber || "Dial a number to get started"}</div>
                  )}
                </div>
              </div>
            </div>
          )}

          {screen === "recents" && (
            <div className="softphone-recents">
              <h2>Call History</h2>
              {recentsError && <div className="error">{recentsError}</div>}
              {recents === null && !recentsError && <p className="hint">Loading…</p>}
              {recents !== null && recents.length === 0 && <p className="hint">No calls yet — dial a number from the Keypad tab.</p>}
              {recents !== null &&
                recents.map((r) => (
                  <div key={r.id} className="softphone-recent-row">
                    <div className="softphone-recent-avatar" onClick={() => redial(r.destinationNumber)}>
                      #
                    </div>
                    <div className="softphone-recent-info" onClick={() => redial(r.destinationNumber)}>
                      <div className="softphone-recent-name">{r.destinationNumber}</div>
                      <div className="softphone-recent-sub">
                        {r.status.replace("_", " ")}
                        {r.durationSeconds > 0 ? ` · ${formatDuration(r.durationSeconds)}` : ""}
                      </div>
                    </div>
                    <div className="softphone-recent-direction" onClick={() => redial(r.destinationNumber)}>
                      <Phone size={12} />
                      {r.direction === "outbound" ? "Outbound call" : "Inbound call"}
                    </div>
                    <div className="softphone-recent-time">{formatWhen(r.startedAt)}</div>
                    <Trash2
                      size={15}
                      className="softphone-delete-icon"
                      onClick={(e) => {
                        e.stopPropagation();
                        deleteRecent(r.id);
                      }}
                    />
                  </div>
                ))}
            </div>
          )}

          {screen === "contacts" && (
            <div className="softphone-recents">
              <h2>Contacts</h2>
              {contactsError && <div className="error">{contactsError}</div>}

              <div className="softphone-add-contact-row">
                <input
                  className="softphone-settings-select"
                  style={{ flex: 1, maxWidth: "none" }}
                  placeholder="Name"
                  value={newContactName}
                  onChange={(e) => setNewContactName(e.target.value)}
                />
                <input
                  className="softphone-settings-select"
                  style={{ flex: 1, maxWidth: "none" }}
                  placeholder="Phone number"
                  value={newContactNumber}
                  onChange={(e) => setNewContactNumber(e.target.value.replace(/[^\d+*#]/g, ""))}
                />
                <button type="button" onClick={addContact} disabled={!newContactNumber.trim()}>
                  Add
                </button>
              </div>

              {contacts === null && !contactsError && <p className="hint">Loading…</p>}
              {contacts !== null && contacts.length === 0 && <p className="hint">No contacts yet — add one above.</p>}
              {contacts !== null &&
                contacts.map((c) => (
                  <div key={c.id} className="softphone-recent-row">
                    <div className="softphone-recent-avatar" onClick={() => redial(c.phoneNumber)}>
                      {(c.firstName || c.phoneNumber)[0]?.toUpperCase()}
                    </div>
                    <div className="softphone-recent-info" onClick={() => redial(c.phoneNumber)}>
                      <div className="softphone-recent-name">
                        {[c.firstName, c.lastName].filter(Boolean).join(" ") || c.phoneNumber}
                      </div>
                      <div className="softphone-recent-sub">{c.phoneNumber}</div>
                    </div>
                    <Trash2
                      size={15}
                      className="softphone-delete-icon"
                      onClick={(e) => {
                        e.stopPropagation();
                        deleteContact(c.id);
                      }}
                    />
                  </div>
                ))}
            </div>
          )}

          {screen === "settings" && (
            <div className="softphone-recents">
              <h2>Devices</h2>
              <p className="hint">Choose which microphone and speakers the softphone uses for calls.</p>

              <div className="softphone-settings-card">
                <div className="softphone-settings-row">
                  <div>
                    <div className="softphone-settings-label">Microphone</div>
                    <div className="softphone-settings-sub">Input device</div>
                  </div>
                  <select
                    className="softphone-settings-select"
                    value={selectedInput}
                    onChange={(e) => changeInputDevice(e.target.value)}
                  >
                    <option value="">System default</option>
                    {inputDevices.map((d) => (
                      <option key={d.deviceId} value={d.deviceId}>
                        {d.label || "Microphone"}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="softphone-settings-row">
                  <div>
                    <div className="softphone-settings-label">Speakers</div>
                    <div className="softphone-settings-sub">Output device</div>
                  </div>
                  <select
                    className="softphone-settings-select"
                    value={selectedOutput}
                    onChange={(e) => changeOutputDevice(e.target.value)}
                  >
                    <option value="">System default</option>
                    {outputDevices.map((d) => (
                      <option key={d.deviceId} value={d.deviceId}>
                        {d.label || "Speaker"}
                      </option>
                    ))}
                  </select>
                </div>
              </div>
              {inputDevices.length === 0 && outputDevices.length === 0 && (
                <p className="hint">
                  No named devices yet — the browser only reveals device names after microphone permission has been
                  granted (which happens the first time you place a call).
                </p>
              )}

              <h2>Sound</h2>
              <div className="softphone-settings-card">
                <div className="softphone-settings-row">
                  <div>
                    <div className="softphone-settings-label">Ringtone</div>
                    <div className="softphone-settings-sub">{ringtoneEnabled ? "Enabled" : "Disabled"}</div>
                  </div>
                  <div className={ringtoneEnabled ? "softphone-toggle on" : "softphone-toggle"} onClick={() => toggleRingtone(!ringtoneEnabled)}>
                    <div className="softphone-toggle-knob" />
                  </div>
                </div>
                <div className="softphone-settings-row">
                  <div>
                    <div className="softphone-settings-label">Alert</div>
                    <div className="softphone-settings-sub">{alertEnabled ? "Enabled" : "Disabled"}</div>
                  </div>
                  <div className={alertEnabled ? "softphone-toggle on" : "softphone-toggle"} onClick={() => toggleAlert(!alertEnabled)}>
                    <div className="softphone-toggle-knob" />
                  </div>
                </div>
                <div className="softphone-settings-row">
                  <div>
                    <div className="softphone-settings-label">Dial Pad Tone</div>
                    <div className="softphone-settings-sub">{dialPadToneEnabled ? "Enabled" : "Disabled"}</div>
                  </div>
                  <div
                    className={dialPadToneEnabled ? "softphone-toggle on" : "softphone-toggle"}
                    onClick={() => toggleDialPadTone(!dialPadToneEnabled)}
                  >
                    <div className="softphone-toggle-knob" />
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function SidebarItem({ icon, label, active, onClick }: { icon: React.ReactNode; label: string; active: boolean; onClick: () => void }) {
  return (
    <div className={active ? "softphone-nav-item active" : "softphone-nav-item"} onClick={onClick}>
      {icon}
      <span>{label}</span>
    </div>
  );
}

function ToggleSwitch({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <div className="softphone-toggle-row">
      <span>{label}</span>
      <div className={checked ? "softphone-toggle on" : "softphone-toggle"} onClick={() => onChange(!checked)}>
        <div className="softphone-toggle-knob" />
      </div>
    </div>
  );
}
