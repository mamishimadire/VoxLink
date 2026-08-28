namespace VoxLink.Api.Email;

public static class EmailTemplates
{
    public static string SetPassword(string firstName, string link) => $"""
        <div style="font-family:Segoe UI,Arial,sans-serif;max-width:480px;margin:0 auto;padding:24px">
          <h2 style="color:#1a1a2e">Welcome to VoxLink</h2>
          <p>Hi {firstName},</p>
          <p>An account has been created for you on VoxLink. Click below to set your password and sign in:</p>
          <p style="margin:24px 0">
            <a href="{link}" style="background:#4f46e5;color:#fff;padding:12px 20px;border-radius:6px;text-decoration:none;font-weight:600">Set your password</a>
          </p>
          <p style="color:#666;font-size:13px">This link expires in 24 hours. If you didn't expect this email, you can ignore it.</p>
        </div>
        """;

    public static string ResetPassword(string firstName, string link) => $"""
        <div style="font-family:Segoe UI,Arial,sans-serif;max-width:480px;margin:0 auto;padding:24px">
          <h2 style="color:#1a1a2e">Reset your VoxLink password</h2>
          <p>Hi {firstName},</p>
          <p>Click below to choose a new password:</p>
          <p style="margin:24px 0">
            <a href="{link}" style="background:#4f46e5;color:#fff;padding:12px 20px;border-radius:6px;text-decoration:none;font-weight:600">Reset password</a>
          </p>
          <p style="color:#666;font-size:13px">This link expires in 1 hour. If you didn't request this, you can ignore it — your password won't change.</p>
        </div>
        """;
}
