export interface VoxLinkClaims {
  sub: string;
  email: string;
  company_id: string;
  is_platform_admin: string;
  [roleClaim: string]: string;
}

const ROLE_CLAIM = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

export function decodeToken(token: string): VoxLinkClaims {
  const payload = token.split(".")[1];
  const json = atob(payload.replace(/-/g, "+").replace(/_/g, "/"));
  return JSON.parse(json);
}

export function getRole(claims: VoxLinkClaims): string {
  return claims[ROLE_CLAIM];
}

export function isPlatformAdmin(claims: VoxLinkClaims): boolean {
  return claims.is_platform_admin === "true";
}

export function isBusinessOwner(claims: VoxLinkClaims): boolean {
  return claims.is_business_owner === "true";
}
