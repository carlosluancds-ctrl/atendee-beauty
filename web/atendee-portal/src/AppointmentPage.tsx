import { useEffect, useState } from "react";
import type { Appointment, Role } from "./types";

type TenantProfile = {
  tenantId: string;
  name: string;
  slug: string;
  timeZoneId: string;
  status: string;
  membership: { displayName: string; role: Role };
};

const STATUS_COLORS: Record<string, string> = {
  PendingConfirmation: "#77736b",
  Confirmed: "#4d6a4c",
  CheckedIn: "#2a5f9c",
  InService: "#008080",
  Completed: "#77736b",
  Cancelled: "#96574b",
  NoShow: "#d4a76a",
  Rescheduled: "#e67e22",
};

const STATUS_LABELS: Record<string, string> = {
  PendingConfirmation: "Pendente",
  Confirmed: "Confirmado",
  CheckedIn: "Check-in",
  InService: "Em serviço",
  Completed: "Finalizado",
  Cancelled: "Cancelado",
  NoShow: "Não compareceu",
  Rescheduled: "Reagendado",
};

async function getAntiforgeryToken(): Promise<string> {
  const response = await fetch("/api/v1/auth/antiforgery", {
    method: "GET", credentials: "same-origin", cache: "no-store",
    referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
  });
  if (!response.ok) throw new Error("Falha ao obter token de segurança.");
  const body = await response.json() as { requestToken?: string } | null;
  if (!body || typeof body.requestToken !== "string" || !body.requestToken)
    throw new Error("Token de segurança não foi disponibilizado.");
  return body.requestToken;
}

async function cancelAppointment(id: string, reason: string): Promise<Response> {
  const token = await getAntiforgeryToken();
  return fetch(`/api/v1/appointments/${id}/cancel`, {
    method: "POST", credentials: "same-origin", cache: "no-store",
    referrerPolicy: "no-referrer",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      "X-CSRF-TOKEN": token
    },
    body: JSON.stringify({ isCustomerCancellation: false, reason })
  });
}

export default function AppointmentPage() {
  const [tenant, setTenant] = useState<TenantProfile | null>(null);
  const [appointments, setAppointments] = useState<Appointment[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [cancelingId, setCancelingId] = useState<string | null>(null);
  const [cancelReason, setCancelReason] = useState("");
  const [appointmentToCancel, setAppointmentToCancel] = useState<Appointment | null>(null);
  const [showCancelModal, setShowCancelModal] = useState(false);

  useEffect(() => {
    let mounted = true;
    async function initialize() {
      try {
        const response = await fetch("/api/v1/tenant", {
          method: "GET", credentials: "same-origin", cache: "no-store",
          referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
        });
        if (response.status === 401) { window.location.replace("/login"); return; }
        if (!response.ok) throw new Error("Não foi possível identificar o salão desta sessão.");
        const profile = await response.json() as TenantProfile;
        if (!mounted) return;
        setTenant(profile);

        const now = new Date();
        const fromUtc = new Date(now.getFullYear(), now.getMonth(), 1).toISOString();
        const toUtc = new Date(now.getFullYear(), now.getMonth() + 1, 1).toISOString();

        const apptResponse = await fetch(`/api/v1/appointments?fromUtc=${encodeURIComponent(fromUtc)}&toUtc=${encodeURIComponent(toUtc)}`, {
          method: "GET", credentials: "same-origin", cache: "no-store",
          referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
        });
        if (apptResponse.status === 401) { window.location.replace("/login"); return; }
        if (!apptResponse.ok) throw new Error("Não foi possível carregar os agendamentos.");
        const body: unknown = await apptResponse.json();
        if (body && typeof body === "object" && "items" in body && Array.isArray((body as any).items)) {
          if (mounted) setAppointments((body as any).items);
        } else if (Array.isArray(body)) {
          if (mounted) setAppointments(body as Appointment[]);
        } else {
          throw new Error("A resposta da lista de agendamentos está em formato inesperado.");
        }
      } catch (cause) {
        if (mounted) setError(cause instanceof Error ? cause.message : "Falha ao carregar os agendamentos.");
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void initialize();
    return () => { mounted = false; };
  }, []);

  const now = new Date();
  const monthName = now.toLocaleString("pt-BR", { month: "long", year: "numeric" });
  const canCancelStatus = ["PendingConfirmation", "Confirmed"] as const;
  const upcomingAppointments = appointments.filter(a => new Date(a.startAtUtc) > new Date());

  async function handleCancelAppointment(id: string) {
    setCancelingId(id);
    setError(null);
    setNotice(null);
    try {
      const response = await cancelAppointment(id, cancelReason || "Cancelado pelo profissional");
      if (response.status === 401) { window.location.replace("/login"); return; }
      if (response.status === 403) throw new Error("Seu perfil não tem permissão para cancelar este agendamento.");
      if (response.status === 404) throw new Error("Agendamento não encontrado.");
      if (!response.ok) throw new Error("Não foi possível cancelar o agendamento.");
      const updated = await response.json() as Appointment;
      setAppointments(prev => prev.map(a => a.id === id ? updated : a));
      setNotice("Agendamento cancelado com sucesso!");
      setCancelReason("");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Falha ao cancelar o agendamento.");
    } finally {
      setCancelingId(null);
      setAppointmentToCancel(null);
      setShowCancelModal(false);
    }
  }

  async function handleConfirmCancel() {
    if (appointmentToCancel) {
      await handleCancelAppointment(appointmentToCancel.id);
    }
  }

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
          <button className="text-button" type="button" onClick={() => { window.location.href = "/login"; }}>Sair</button>
        </div>
      </header>

      <section className="workspace-content">
        <div className="workspace-heading">
          <div>
            <div className="eyebrow">CALENDÁRIO</div>
            <h1>Agendamentos</h1>
            <p>{monthName} • {upcomingAppointments.length} agendamento(s) neste mês</p>
          </div>
        </div>

        {error && <p className="feedback error" role="alert">{error}</p>}
        {notice && <p className="feedback success" role="status">{notice}</p>}

        {loading ? <div className="empty-state">Carregando os agendamentos…</div> :
          upcomingAppointments.length === 0 ? (
            <div className="empty-state"><strong>Nenhum agendamento neste mês</strong>
              <p>Os agendamentos aparecerão aqui quando houver clientes e serviços cadastrados.</p></div>
          ) : (
            <div className="appointment-grid">
              {upcomingAppointments.map(appointment => {
                const status = appointment.appointmentStatus;
                const statusColor = STATUS_COLORS[status] || STATUS_COLORS.PendingConfirmation;
                const startTime = new Date(appointment.startAtUtc);
                const isCancellable = canCancelStatus.includes(status as typeof canCancelStatus[number]);
                return (
                  <article className="appointment-card" key={appointment.id}>
                    <div className="appointment-card-top">
                      <div>
                        <h3>{appointment.service.name}</h3>
                        <p className="appointment-time">
                          {startTime.toLocaleString("pt-BR", {
                            weekday: "short",
                            day: "numeric",
                            hour: "2-digit",
                            minute: "2-digit"
                          })}
                        </p>
                      </div>
                      <span className={"status-badge"} style={{ backgroundColor: statusColor }}>
                        {STATUS_LABELS[status] || status}
                      </span>
                    </div>
                    <div className="appointment-details">
                      <div className="appointment-party">
                        <p className="appointment-label"> Cliente</p>
                        <p className="appointment-name">{appointment.customer.fullName}</p>
                      </div>
                      <div className="appointment-party">
                        <p className="appointment-label"> Profissional</p>
                        <p className="appointment-name">{appointment.professional.displayName}</p>
                      </div>
                      <div className="appointment-party">
                        <p className="appointment-label"> Duração</p>
                        <p className="appointment-name">{appointment.service.durationMinutes} min</p>
                      </div>
                    </div>
                    {isCancellable && (
                      <div className="appointment-actions">
                        <button
                          className="action-button danger"
                          onClick={() => { setAppointmentToCancel(appointment); setShowCancelModal(true); }}
                          disabled={cancelingId === appointment.id}
                        >
                          {cancelingId === appointment.id ? "Cancelando..." : "Cancelar"}
                        </button>
                      </div>
                    )}
                  </article>
                );
              })}
            </div>
          )}
      </section>

      {showCancelModal && appointmentToCancel && (
        <div className="dialog-backdrop" onClick={() => { setShowCancelModal(false); setAppointmentToCancel(null); }}>
          <dialog className="dialog-card" open>
            <h2>Cancelar Agendamento</h2>
            <p>Tem certeza que deseja cancelar este agendamento?</p>
            <p><strong>Cliente:</strong> {appointmentToCancel.customer.fullName}</p>
            <p><strong>Serviço:</strong> {appointmentToCancel.service.name}</p>
            <p><strong>Horário:</strong> {new Date(appointmentToCancel.startAtUtc).toLocaleString("pt-BR", {
              weekday: "short", day: "numeric", hour: "2-digit", minute: "2-digit"
            })}</p>
            <form onSubmit={(e) => { e.preventDefault(); handleConfirmCancel(); }}>
              <div>
                <label htmlFor="cancelReason">Motivo (opcional):</label>
                <textarea
                  id="cancelReason"
                  value={cancelReason}
                  onChange={e => setCancelReason(e.target.value)}
                  rows={3}
                  placeholder="Informe o motivo do cancelamento..."
                />
              </div>
              <div className="dialog-actions">
                <button type="button" className="secondary-button" onClick={() => { setShowCancelModal(false); setAppointmentToCancel(null); setCancelReason(""); }}>
                  Voltar
                </button>
                <button type="submit" disabled={cancelingId === appointmentToCancel.id}>
                  {cancelingId === appointmentToCancel.id ? "Cancelando..." : "Confirmar Cancelamento"}
                </button>
              </div>
            </form>
          </dialog>
        </div>
      )}

      <footer>© {new Date().getFullYear()} Atendee Beauty</footer>
    </main>
  );
}