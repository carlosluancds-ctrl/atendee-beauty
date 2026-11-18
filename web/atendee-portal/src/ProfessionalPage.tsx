import { useEffect, useState } from "react";
import type { FormEvent } from "react";

type Role = "Owner" | "Manager" | "Receptionist" | "Professional";
type TenantProfile = {
  name: string;
  membership: { displayName: string; role: Role };
};
type ProfessionalProfile = {
  id: string;
  identityUserId: string;
  displayName: string;
  isActive: boolean;
  serviceIds: string[];
  createdAtUtc: string;
  updatedAtUtc: string;
};
type SalonService = {
  id: string;
  name: string;
  durationMinutes: number;
  priceMinorUnits: number;
  currency: string;
  isActive: boolean;
};

async function readJson(response: Response): Promise<unknown> {
  try { return await response.json(); } catch { return null; }
}

async function getAntiforgeryToken(): Promise<string> {
  const response = await fetch("/api/v1/auth/antiforgery", {
    method: "GET", credentials: "same-origin", cache: "no-store",
    referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
  });
  if (!response.ok) throw new Error("Não foi possível preparar a validação de segurança.");
  const body = await readJson(response) as { requestToken?: unknown } | null;
  if (!body || typeof body.requestToken !== "string" || !body.requestToken)
    throw new Error("A validação de segurança não foi disponibilizada.");
  return body.requestToken;
}

export default function ProfessionalPage() {
  const [tenant, setTenant] = useState<TenantProfile | null>(null);
  const [profiles, setProfiles] = useState<ProfessionalProfile[]>([]);
  const [services, setServices] = useState<SalonService[]>([]);
  const [ownProfile, setOwnProfile] = useState<ProfessionalProfile | null>(null);
  const [selections, setSelections] = useState<Record<string, string[]>>({});
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

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

        if (currentTenant.membership.role === "Professional") {
          const response = await fetch("/api/v1/professionals/me", {
            method: "GET", credentials: "same-origin", cache: "no-store",
            referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
          });
          if (response.status === 401) { window.location.replace("/login"); return; }
          if (response.status === 404) throw new Error("Seu perfil profissional ainda não foi provisionado.");
          if (response.status === 403) throw new Error("Seu perfil não tem acesso a esta área.");
          if (!response.ok) throw new Error("Não foi possível carregar seu perfil profissional.");
          const profile = await response.json() as ProfessionalProfile;
          if (mounted) setOwnProfile(profile);
          return;
        }

        if (!["Owner", "Manager", "Receptionist"].includes(currentTenant.membership.role))
          throw new Error("Seu perfil não tem acesso à equipe deste salão.");

        const [teamResponse, servicesResponse] = await Promise.all([
          fetch("/api/v1/professionals", {
            method: "GET", credentials: "same-origin", cache: "no-store",
            referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
          }),
          fetch("/api/v1/services", {
            method: "GET", credentials: "same-origin", cache: "no-store",
            referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
          })
        ]);
        if (teamResponse.status === 401 || servicesResponse.status === 401) {
          window.location.replace("/login"); return;
        }
        if (teamResponse.status === 403 || servicesResponse.status === 403)
          throw new Error("Seu perfil não tem permissão para consultar a equipe ou os serviços.");
        if (!teamResponse.ok || !servicesResponse.ok)
          throw new Error("Não foi possível carregar os dados da equipe.");
        const teamBody: unknown = await teamResponse.json();
        const serviceBody: unknown = await servicesResponse.json();
        if (!Array.isArray(teamBody) || !Array.isArray(serviceBody))
          throw new Error("A resposta da equipe está em formato inesperado.");
        const team = teamBody as ProfessionalProfile[];
        if (mounted) {
          setProfiles(team);
          setServices(serviceBody as SalonService[]);
          setSelections(Object.fromEntries(team.map(profile => [profile.id, profile.serviceIds])));
        }
      } catch (cause) {
        if (mounted) setError(cause instanceof Error ? cause.message : "Falha ao carregar a equipe.");
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void initialize();
    return () => { mounted = false; };
  }, []);

  function toggleService(profileId: string, serviceId: string, checked: boolean) {
    setSelections(current => {
      const selected = current[profileId] ?? [];
      return {
        ...current,
        [profileId]: checked
          ? [...selected, serviceId]
          : selected.filter(id => id !== serviceId)
      };
    });
  }

  async function saveAssignments(event: FormEvent<HTMLFormElement>, profile: ProfessionalProfile) {
    event.preventDefault();
    setError(null);
    setNotice(null);
    setBusyId(profile.id);
    try {
      const token = await getAntiforgeryToken();
      const response = await fetch("/api/v1/professionals/" + profile.id + "/services", {
        method: "PUT", credentials: "same-origin", cache: "no-store",
        referrerPolicy: "no-referrer",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
          "X-CSRF-TOKEN": token
        },
        body: JSON.stringify({ serviceIds: selections[profile.id] ?? [] })
      });
      if (response.status === 401) { window.location.replace("/login"); return; }
      if (response.status === 403) throw new Error("Seu perfil não tem permissão para alterar atribuições.");
      if (response.status === 400) throw new Error("Uma atribuição é inválida. Atualize os serviços e tente novamente.");
      if (!response.ok) throw new Error("Não foi possível salvar as atribuições.");
      const updated = await response.json() as ProfessionalProfile;
      setProfiles(current => current.map(item => item.id === updated.id ? updated : item));
      setSelections(current => ({ ...current, [updated.id]: updated.serviceIds }));
      setNotice("Serviços de " + updated.displayName + " atualizados.");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível salvar as atribuições.");
    } finally {
      setBusyId(null);
    }
  }

  async function logout() {
    try {
      const token = await getAntiforgeryToken();
      await fetch("/api/v1/auth/logout", {
        method: "POST", credentials: "same-origin", cache: "no-store",
        referrerPolicy: "no-referrer", headers: { "X-CSRF-TOKEN": token }
      });
    } finally { window.location.replace("/login"); }
  }

  const isProfessional = tenant?.membership.role === "Professional";
  const canManage = tenant?.membership.role === "Owner" || tenant?.membership.role === "Manager";
  const activeServices = services.filter(service => service.isActive);

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
          <a className="text-button" href="/services">Serviços</a>
          <button className="text-button" type="button" onClick={logout}>Sair</button>
        </div>
      </header>

      <section className="workspace-content">
        <div className="workspace-heading">
          <div>
            <div className="eyebrow">{isProfessional ? "MEU ESPAÇO" : "GESTÃO DO SALÃO"}</div>
            <h1>{isProfessional ? "Meus serviços" : "Equipe"}</h1>
            <p>{isProfessional
              ? "Consulte seu perfil e a quantidade de serviços vinculados a você."
              : "Consulte a equipe e gerencie os serviços que cada profissional pode executar."}</p>
          </div>
          {!isProfessional && <div className="catalog-summary">
            <strong>{profiles.length}</strong><span>profissionais</span>
          </div>}
        </div>

        {error && <p className="feedback error" role="alert">{error}</p>}
        {notice && <p className="feedback success" role="status">{notice}</p>}

        {loading ? <div className="empty-state">Carregando…</div> : isProfessional ? (
          ownProfile ? (
            <section className="professional-self-card" aria-labelledby="own-profile-title">
              <div className="service-card-top">
                <h2 id="own-profile-title">{ownProfile.displayName}</h2>
                <span className={"service-status" + (ownProfile.isActive ? " active" : "")}>
                  {ownProfile.isActive ? "Ativo" : "Inativo"}
                </span>
              </div>
              <p>Perfil profissional vinculado à sua identidade nesta empresa.</p>
              <div className="professional-self-count">
                <strong>{ownProfile.serviceIds.length}</strong>
                <span>serviço(s) atribuído(s)</span>
              </div>
            </section>
          ) : <div className="empty-state">Seu perfil profissional não está disponível.</div>
        ) : profiles.length === 0 ? (
          <div className="empty-state">
            <strong>Nenhum perfil profissional cadastrado</strong>
            <p>Os perfis precisam estar vinculados a uma associação Professional ativa do mesmo salão.</p>
          </div>
        ) : (
          <div className="professional-grid">
            {profiles.map(profile => (
              <article className="professional-card" key={profile.id}>
                <div className="service-card-top">
                  <div>
                    <h2>{profile.displayName}</h2>
                    <p>{profile.serviceIds.length} serviço(s) atribuído(s)</p>
                  </div>
                  <span className={"service-status" + (profile.isActive ? " active" : "")}>
                    {profile.isActive ? "Ativo" : "Inativo"}
                  </span>
                </div>
                {canManage ? (
                  <form className="professional-assignment-form"
                    onSubmit={event => void saveAssignments(event, profile)}>
                    <fieldset disabled={busyId === profile.id || !profile.isActive}>
                      <legend>Serviços que pode executar</legend>
                      {activeServices.length === 0 ? <p className="field-hint">Não há serviços ativos para atribuir.</p> :
                        activeServices.map(service => (
                          <label className="professional-service-option" key={service.id}>
                            <input type="checkbox"
                              checked={(selections[profile.id] ?? []).includes(service.id)}
                              onChange={event => toggleService(profile.id, service.id, event.target.checked)} />
                            <span>{service.name}</span>
                          </label>
                        ))}
                    </fieldset>
                    <button type="submit" disabled={busyId === profile.id || !profile.isActive}>
                      {busyId === profile.id ? "Salvando…" : "Salvar serviços"}
                    </button>
                  </form>
                ) : (
                  <div className="professional-readonly-services">
                    <strong>Serviços atribuídos</strong>
                    {profile.serviceIds.length === 0 ? <p>Nenhum serviço atribuído.</p> : (
                      <ul className="professional-readonly-list">
                        {profile.serviceIds.map(id => (
                          <li key={id}>{services.find(service => service.id === id)?.name ?? "Serviço não disponível"}</li>
                        ))}
                      </ul>
                    )}
                    <p className="professional-permission-note">Alterações são restritas a Owner e Manager.</p>
                  </div>
                )}
              </article>
            ))}
          </div>
        )}
      </section>
      <footer>© {new Date().getFullYear()} Atendee Beauty</footer>
    </main>
  );
}
