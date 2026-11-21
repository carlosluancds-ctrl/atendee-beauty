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

export type Customer = {
  id: string;
  fullName: string;
  phoneNumber: string | null;
  emailAddress: string | null;
  isActive: boolean;
  createdAtUtc: string;
};

export type Appointment = {
  id: string;
  appointmentStatus: "PendingConfirmation" | "Confirmed" | "CheckedIn" | "InService" | "Completed" | "Cancelled" | "NoShow" | "Rescheduled";
  customer: {
    id: string;
    fullName: string;
  };
  professional: {
    id: string;
    displayName: string;
  };
  service: {
    id: string;
    name: string;
    durationMinutes: number;
  };
  startAtUtc: string;
  endAtUtc: string;
  createdAtUtc: string;
};