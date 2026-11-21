import { useEffect, useState } from "react";
import type { Appointment, Role, Service, Professional } from "./types";

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

async function rescheduleAppointment(id: string, newStartAtUtc: Date): Promise<Response> {
  const token = await getAntiforgeryToken();
  return fetch(`/api/v1/appointments/${id}/reschedule`, {
    method: "POST", credentials: "same-origin", cache: "no-store",
    referrerPolicy: "no-referrer",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      "X-CSRF-TOKEN": token
    },
    body: JSON.stringify({ newStartAtUtc: newStartAtUtc.toISOString() })
  });
}

async function loadServicesAndProfessionals(): Promise<{ services: Service[]; professionals: Professional[] }> {
  const [servicesRes, professionalsRes] = await Promise.all([
    fetch("/api/v1/services", {
      method: "GET", credentials: "same-origin", cache: "no-store",
      referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
    }),
    fetch("/api/v1/professionals", {
      method: "GET", credentials: "same-origin", cache: "no-store",
      referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
    })
  ]);

  let services: Service[] = [];
  let professionals: Professional[] = [];

  if (servicesRes.ok) {
    const body = await servicesRes.json() as unknown;
    const list = body && typeof body === "object" && "items" in body
      ? (body as any).items as Service[]
      : Array.isArray(body) ? body as Service[]
      : [];
    services = list.filter(s => s.isActive);
  }

  if (professionalsRes.ok) {
    const body = await professionalsRes.json() as unknown;
    const list = body && typeof body === "object" && "items" in body
      ? (body as any).items as Professional[]
      : Array.isArray(body) ? body as Professional[]
      : [];
    professionals = list.filter(p => p.isActive);
  }

  return { services, professionals };
}

export default function AppointmentPage() {
  const [tenant, setTenant] = useState<TenantProfile | null>(null);
  const [appointments, setAppointments] = useState<Appointment[]>([]);
  const [loading, setLoading] = useState(true);
  // Services and professionals loaded for future availability checking
  const [_services, _setServices] = useState<Service[]>([]);
  const [_professionals, _setProfessionals] = useState<Professional[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [cancelingId, setCancelingId] = useState<string | null>(null);
  const [reschedulingId, setReschedulingId] = useState<string | null>(null);
  const [cancelReason, setCancelReason] = useState("");
  const [rescheduleDate, setRescheduleDate] = useState<string>("");
  const [rescheduleTime, setRescheduleTime] = useState<string>("09:00");
  const [appointmentToCancel, setAppointmentToCancel] = useState<Appointment | null>(null);
  const [appointmentToReschedule, setAppointmentToReschedule] = useState<Appointment | null>(null);
  const [showCancelModal, setShowCancelModal] = useState(false);
  const [showRescheduleModal, setShowRescheduleModal] = useState(false);

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
        const apptBody: unknown = await apptResponse.json();
        let appts: Appointment[] = [];
        if (apptBody && typeof apptBody === "object" && "items" in apptBody && Array.isArray((apptBody as any).items)) {
          appts = (apptBody as any).items;
        } else if (Array.isArray(apptBody)) {
          appts = apptBody as Appointment[];
        } else {
          throw new Error("A resposta da lista de agendamentos está em formato inesperado.");
        }
        if (mounted) setAppointments(appts);

        await loadServicesAndProfessionals(); // Preload for future availability checking
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
  const today = new Date().toISOString().split("T")[0];
  const monthName = now.toLocaleString("pt-BR", { month: "long", year: "numeric" });
  const canCancelStatus = ["PendingConfirmation", "Confirmed"] as const;
  const canRescheduleStatus = ["PendingConfirmation", "Confirmed"] as const;
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

  async function handleReschedule() {
    if (!appointmentToReschedule || !rescheduleDate || !rescheduleTime) return;

    const [hours, minutes] = rescheduleTime.split(":").map(Number);
    const newDateTime = new Date(rescheduleDate);
    newDateTime.setHours(hours || 0, minutes || 0, 0, 0);

    if (newDateTime <= now) {
      setError("A data/hora de reagendamento deve ser futura.");
      return;
    }

    setReschedulingId(appointmentToReschedule.id);
    setError(null);
    setNotice(null);

    try {
      const response = await rescheduleAppointment(appointmentToReschedule.id, newDateTime);
      if (response.status === 401) { window.location.replace("/login"); return; }
      if (response.status === 403) throw new Error("Seu perfil não tem permissão para reagendar este agendamento.");
      if (response.status === 404) throw new Error("Agendamento não encontrado.");
      if (response.status === 409) throw new Error("Conflito de horário - o profissional já tem outro agendamento.");
      if (!response.ok) throw new Error("Não foi possível reagendar o agendamento.");
      const updated = await response.json() as Appointment;
      setAppointments(prev => prev.map(a => a.id === appointmentToReschedule.id ? updated : a));
      setNotice("Agendamento reagendado com sucesso!");
      setRescheduleDate("");
      setRescheduleTime("09:00");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Falha ao reagendar o agendamento.");
    } finally {
      setReschedulingId(null);
      setAppointmentToReschedule(null);
      setShowRescheduleModal(false);
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
                const isReschedulable = canRescheduleStatus.includes(status as typeof canRescheduleStatus[number]);
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
                    {(isCancellable || isReschedulable) && (
                      <div className="appointment-actions">
                        {isReschedulable && (
                          <button
                            className="action-button"
                            onClick={() => { setAppointmentToReschedule(appointment); setRescheduleDate(""); setShowRescheduleModal(true); }}
                            disabled={reschedulingId === appointment.id}
                          >
                            {reschedulingId === appointment.id ? "Reagendando..." : "Reagendar"}
                          </button>
                        )}
                        {isCancellable && (
                          <button
                            className="action-button danger"
                            onClick={() => { setAppointmentToCancel(appointment); setShowCancelModal(true); }}
                            disabled={cancelingId === appointment.id}
                          >
                            {cancelingId === appointment.id ? "Cancelando..." : "Cancelar"}
                          </button>
                        )}
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
            <p><strong>Horário original:</strong> {new Date(appointmentToCancel.startAtUtc).toLocaleString("pt-BR", {
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

      {showRescheduleModal && appointmentToReschedule && (
        <div className="dialog-backdrop" onClick={() => { setShowRescheduleModal(false); setAppointmentToReschedule(null); setRescheduleDate(""); }}>
          <dialog className="dialog-card" open>
            <h2>Reagendar Agendamento</h2>
            <p>Reagende o agendamento para uma nova data e hora.</p>
            <p><strong>Cliente:</strong> {appointmentToReschedule.customer.fullName}</p>
            <p><strong>Serviço:</strong> {appointmentToReschedule.service.name} ({appointmentToReschedule.service.durationMinutes} min)</p>
            <div>
              <label htmlFor="rescheduleDate">Nova data:</label>
              <input
                type="date"
                id="rescheduleDate"
                value={rescheduleDate}
                onChange={e => setRescheduleDate(e.target.value)}
                min={today}
                required
              />
            </div>
            <div>
              <label htmlFor="rescheduleTime">Nova hora:</label>
              <input
                type="time"
                id="rescheduleTime"
                value={rescheduleTime}
                onChange={e => setRescheduleTime(e.target.value)}
                required
              />
            </div>
            <div className="dialog-actions">
              <button type="button" className="secondary-button" onClick={() => { setShowRescheduleModal(false); setAppointmentToReschedule(null); setRescheduleDate(""); setRescheduleTime("09:00"); }}>
                Voltar
              </button>
              <button type="submit" onClick={handleReschedule} disabled={reschedulingId === appointmentToReschedule.id}>
                {reschedulingId === appointmentToReschedule.id ? "Reagendando..." : "Confirmar Reagendamento"}
              </button>
            </div>
          </dialog>
        </div>
      )}

      <footer>© {new Date().getFullYear()} Atendee Beauty</footer>
    </main>
  );
}