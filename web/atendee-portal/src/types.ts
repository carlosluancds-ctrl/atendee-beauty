export type Role = "Owner" | "Manager" | "Receptionist" | "Professional";

export type TenantMember = {
  identityUserId: string;
  displayName: string;
  role: Role;
  isActive: boolean;
};

export type ChangeRoleRequest = {
  role: Role;
};

export type DeactivateMemberRequest = {
  reason: string;
};

export type ReactivateMemberRequest = {
  reason: string;
};