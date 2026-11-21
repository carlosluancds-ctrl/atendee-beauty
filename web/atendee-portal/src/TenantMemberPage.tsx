import { useEffect, useState } from "react";
import type { Role, TenantMember, ChangeRoleRequest, DeactivateMemberRequest, ReactivateMemberRequest } from "./types";

async function readJson(response: Response): Promise<unknown> {
  try { return await response.json(); } catch { return null; }
}

async function getAntiforgeryToken(): Promise<string> {
  const response = await fetch("/api/v1/auth/antiforgery", {
    method: "GET", credentials: "same-origin", cache: "no-store",
    referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
  });
  if (!response.ok) throw new Error("Não foi possível preparar a validação de segurança.");
  const body = await readJson(response) as { requestToken?: string } | null;
  if (!body || !body.requestToken)
    throw new Error("A validação de segurança não foi disponibilizada.");
  return body.requestToken;
}

const roleLabels: Record<Role, string> = {
  Owner: "Proprietário",
  Manager: "Gerente",
  Receptionist: "Recepcionista",
  Professional: "Profissional"
};

const roleColors: Record<Role, string> = {
  Owner: "owner",
  Manager: "manager",
  Receptionist: "receptionist",
  Professional: "professional"
};

async function changeMemberRole(identityUserId: string, role: Role): Promise<boolean> {
  const token = await getAntiforgeryToken();
  const response = await fetch(`/api/v1/tenant/members/${identityUserId}/role`, {
    method: "PUT", credentials: "same-origin", cache: "no-store",
    referrerPolicy: "no-referrer",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      "X-CSRF-TOKEN": token
    },
    body: JSON.stringify({ role } as ChangeRoleRequest)
  });
  if (response.status === 401) { window.location.replace("/login"); return false; }
  if (response.status === 403) throw new Error("Seu perfil não tem permissão para alterar o papel deste membro.");
  if (response.status === 400) throw new Error("Solicitação inválida.");
  if (response.status === 404) return false;
  if (response.status === 409) throw new Error("Não foi possível alterar o papel devido a restrições de segurança.");
  if (!response.ok) throw new Error("Não foi possível alterar o papel do membro.");
  return true;
}

async function deactivateMember(identityUserId: string, reason: string): Promise<boolean> {
  const token = await getAntiforgeryToken();
  const response = await fetch(`/api/v1/tenant/members/${identityUserId}/deactivate`, {
    method: "POST", credentials: "same-origin", cache: "no-store",
    referrerPolicy: "no-referrer",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      "X-CSRF-TOKEN": token
    },
    body: JSON.stringify({ reason } as DeactivateMemberRequest)
  });
  if (response.status === 401) { window.location.replace("/login"); return false; }
  if (response.status === 403) throw new Error("Seu perfil não tem permissão para desativar este membro.");
  if (response.status === 400) throw new Error("Solicitação inválida.");
  if (response.status === 404) return false;
  if (response.status === 409) throw new Error("Não foi possível desativar devido a restrições de segurança.");
  if (!response.ok) throw new Error("Não foi possível desativar o membro.");
  return true;
}

async function reactivateMember(identityUserId: string, reason: string): Promise<boolean> {
  const token = await getAntiforgeryToken();
  const response = await fetch(`/api/v1/tenant/members/${identityUserId}/reactivate`, {
    method: "POST", credentials: "same-origin", cache: "no-store",
    referrerPolicy: "no-referrer",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      "X-CSRF-TOKEN": token
    },
    body: JSON.stringify({ reason } as ReactivateMemberRequest)
  });
  if (response.status === 401) { window.location.replace("/login"); return false; }
  if (response.status === 403) throw new Error("Seu perfil não tem permissão para reativar este membro.");
  if (response.status === 400) throw new Error("Solicitação inválida.");
  if (response.status === 404) return false;
  if (response.status === 409) throw new Error("Não foi possível reativar devido a restrições de segurança.");
  if (!response.ok) throw new Error("Não foi possível reativar o membro.");
  return true;
}

type TenantProfile = {
  name: string;
  membership: { displayName: string; role: Role };
};

type MemberDialog = {
  open: boolean;
  member: TenantMember | null;
  action: "change-role" | "deactivate" | "reactivate" | null;
};

export default function TenantMemberPage() {
  const [tenant, setTenant] = useState<TenantProfile | null>(null);
  const [members, setMembers] = useState<TenantMember[]>([]);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [dialog, setDialog] = useState<MemberDialog>({ open: false, member: null, action: null });
  const [formData, setFormData] = useState<{
    role: Role;
    reason: string;
  }>({ role: "Professional", reason: "" });

  useEffect(() => {
    let mounted = true;
    async function initialize() {
      try {
        const tenantResponse = await fetch("/api/v1/tenant", {
          method: "GET", credentials: "same-origin", cache: "no-store",
          referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
        });
        if (tenantResponse.status === 401) { window.location.replace("/login"); return; }
        if (!tenantResponse.ok) throw new Error("Não foi possível identificar o salão desta sessão.");
        const currentTenant = await tenantResponse.json() as TenantProfile;
        if (!mounted) return;
        setTenant(currentTenant);

        if (currentTenant.membership.role !== "Owner" && currentTenant.membership.role !== "Manager")
          throw new Error("Seu perfil não tem acesso à gestão de membros.");

        const teamResponse = await fetch("/api/v1/professionals", {
          method: "GET", credentials: "same-origin", cache: "no-store",
          referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
        });
        if (teamResponse.status === 401) { window.location.replace("/login"); return; }
        if (teamResponse.status === 403) throw new Error("Seu perfil não tem permissão para consultar a equipe.");
        if (!teamResponse.ok) throw new Error("Não foi possível carregar os membros.");
        const teamBody: unknown = await teamResponse.json();
        if (!Array.isArray(teamBody)) throw new Error("A resposta da equipe está em formato inesperado.");
        if (mounted) {
          setMembers(teamBody as TenantMember[]);
        }
      } catch (cause) {
        if (mounted) setError(cause instanceof Error ? cause.message : "Falha ao carregar os membros.");
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void initialize();
    return () => { mounted = false; };
  }, []);

  const canManage = tenant?.membership.role === "Owner" || tenant?.membership.role === "Manager";
  const isOwner = tenant?.membership.role === "Owner";

  const canDeactivate = (member: TenantMember): boolean => {
    if (!canManage) return false;
    if (member.isActive === false) return false;
    if (isOwner && member.identityUserId === tenant?.membership.displayName) return false;
    if (member.role === "Owner") return false;
    if (tenant?.membership.role === "Manager" &&
        member.role !== "Receptionist" &&
        member.role !== "Professional") return false;
    return true;
  };

  const canReactivate = (member: TenantMember): boolean => {
    if (!canManage) return false;
    if (member.isActive === true) return false;
    if (tenant?.membership.role === "Manager" &&
        member.role !== "Receptionist" &&
        member.role !== "Professional") return false;
    return true;
  };

  const canChangeRole = (member: TenantMember): boolean => {
    if (!canManage) return false;
    if (member.identityUserId === "") return false;
    if (isOwner && member.role === "Owner") return false;
    if (tenant?.membership.role === "Manager" &&
        (member.role === "Owner" || member.role === "Manager")) return false;
    return true;
  };

  async function performAction() {
    if (!dialog.member || !dialog.action) return;

    const member = dialog.member;
    const action = dialog.action;

    setError(null);
    setNotice(null);
    setBusyId(member.identityUserId);

    try {
      if (action === "change-role") {
        const success = await changeMemberRole(member.identityUserId, formData.role);
        if (!success) throw new Error("Membro não encontrado.");
        setMembers(current => current.map(m =>
          m.identityUserId === member.identityUserId
            ? { ...m, role: formData.role }
            : m
        ));
        setNotice(`Papel de ${member.displayName} alterado para ${roleLabels[formData.role]}.`);
      } else if (action === "deactivate") {
        const success = await deactivateMember(member.identityUserId, formData.reason);
        if (!success) throw new Error("Membro não encontrado.");
        setMembers(current => current.filter(m => m.identityUserId !== member.identityUserId));
        setNotice(`${member.displayName} foi desativado(a).`);
      } else if (action === "reactivate") {
        const success = await reactivateMember(member.identityUserId, formData.reason);
        if (!success) throw new Error("Membro não encontrado.");
        setMembers(current => [...current, {
          identityUserId: member.identityUserId,
          displayName: member.displayName,
          role: member.role,
          isActive: true
        }]);
        setNotice(`${member.displayName} foi reativado(a).`);
      }
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Ação falhou.");
    } finally {
      setBusyId(null);
      setDialog({ open: false, member: null, action: null });
      setFormData({ role: "Professional", reason: "" });
    }
  }

  const activeRoles = ["Owner", "Manager", "Receptionist", "Professional"] as Role[];

  function openDialog(action: "change-role" | "deactivate" | "reactivate", member: TenantMember | null) {
    if (!member) return;
    setDialog({ open: true, member, action });
    setFormData({ role: member.role, reason: "" });
  }

  function closeDialog() {
    setDialog({ open: false, member: null, action: null });
    setFormData({ role: "Professional", reason: "" });
  }

  if (loading) return <div className="empty-state">Carregando…</div>;

  if (!canManage) return <div className="empty-state">Você não tem permissão para gerenciar membros.</div>;

  return (
    <main className="workspace-shell">
      <header className="workspace-header">
        <div className="brand-lockup">
          <span className="brand-mark" aria-hidden="true">a</span>
          <span className="brand-name">atendee<span>beauty</span></span>
        </div>
        <div className="workspace-user">
          <div>
            <strong>{tenant?.name ?? "Seu salão"}</strong>
            <span>{tenant?.membership.displayName ?? ""} · {tenant?.membership.role ?? ""}</span>
          </div>
          <button className="text-button" type="button" onClick={() => window.location.replace("/team")}>Voltar</button>
        </div>
      </header>

      <section className="workspace-content">
        <div className="workspace-heading">
          <div>
            <div className="eyebrow">GESTÃO DE MEMBROS</div>
            <h1>Membros do Tenant</h1>
            <p>Atualize papéis, ative ou desative membros conforme necessário.</p>
          </div>
        </div>

        {error && <p className="feedback error" role="alert">{error}</p>}
        {notice && <p className="feedback success" role="status">{notice}</p>}

        {members.length === 0 ? (
          <div className="empty-state">
            <strong>Nenhum membro cadastrado</strong>
            <p>Não há membros ativos neste tenant.</p>
          </div>
        ) : (
          <div className="tenant-member-grid">
            {members.map(member => (
              <article className="tenant-member-card" key={member.identityUserId}>
                <div className="tenant-member-top">
                  <div>
                    <h2>{member.displayName}</h2>
                    <span className={`role-badge role-${roleColors[member.role]}`}>
                      {roleLabels[member.role]}
                    </span>
                  </div>
                  {member.isActive ? (
                    <span className="member-status active">Ativo</span>
                  ) : (
                    <span className="member-status inactive">Inativo</span>
                  )}
                </div>

                <div className="member-actions">
                  {canChangeRole(member) && (
                    <button className="action-button" onClick={() => openDialog("change-role", member)}>
                      Alterar papel
                    </button>
                  )}
                  {canDeactivate(member) && (
                    <button className="action-button danger" onClick={() => openDialog("deactivate", member)}>
                      Desativar
                    </button>
                  )}
                  {canReactivate(member) && (
                    <button className="action-button" onClick={() => openDialog("reactivate", member)}>
                      Reativar
                    </button>
                  )}
                </div>
              </article>
            ))}
          </div>
        )}
      </section>

      {dialog.open && dialog.member && (
        <div className="dialog-backdrop" onClick={closeDialog}>
          <div className="dialog-card" onClick={e => e.stopPropagation()}>
            <h2>{dialog.action === "change-role" && "Alterar papel"}
              {dialog.action === "deactivate" && "Desativar membro"}
              {dialog.action === "reactivate" && "Reativar membro"}</h2>

            {dialog.action === "change-role" && (
              <form onSubmit={e => { e.preventDefault(); performAction(); }}>
                <label htmlFor="role-select">Novo papel</label>
                <select
                  id="role-select"
                  value={formData.role}
                  onChange={e => setFormData({ ...formData, role: e.target.value as Role })}
                  disabled={busyId !== dialog.member!.identityUserId}
                >
                  {activeRoles.map(role => (
                    <option key={role} value={role}>{roleLabels[role]}</option>
                  ))}
                </select>
                <button type="submit" disabled={busyId === dialog.member!.identityUserId}>
                  {busyId === dialog.member!.identityUserId ? "Salvando…" : "Salvar alterações"}
                </button>
              </form>
            )}

            {(dialog.action === "deactivate" || dialog.action === "reactivate") && (
              <form onSubmit={e => {
                e.preventDefault();
                performAction();
              }}>
                <label htmlFor="reason-input">
                  Motivo
                  <input
                    id="reason-input"
                    type="text"
                    value={formData.reason}
                    onChange={e => setFormData({ ...formData, reason: e.target.value })}
                    placeholder={dialog.action === "deactivate"
                      ? "Razão para desativação"
                      : "Razão para reativação"}
                    required
                    disabled={busyId !== dialog.member!.identityUserId}
                  />
                </label>
                <button type="submit" disabled={!formData.reason || busyId === dialog.member!.identityUserId}>
                  {busyId === dialog.member!.identityUserId
                    ? (dialog.action === "deactivate" ? "Desativando…" : "Reativando…")
                    : (dialog.action === "deactivate" ? "Desativar" : "Reativar")}
                </button>
              </form>
            )}
          </div>
        </div>
      )}

      <footer>© {new Date().getFullYear()} Atendee Beauty</footer>
    </main>
  );
}