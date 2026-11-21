import { useEffect, useState } from "react";
import type { Appointment, Customer } from "./types";

type CustomerAppointmentResponse = {
  appointment: Appointment;
  customer: Customer;
};

export default function CustomerAppointmentPage() {
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [appointment, setAppointment] = useState<Appointment | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [showCancelModal, setShowCancelModal] = useState(false);
  const [cancelReason, setCancelReason] = useState("");

  // Booking code from URL query parameter
  const bookingCode = new URLSearchParams(window.location.search).get("code");

  useEffect(() => {
    let mounted = true;
    async function initialize() {
      try {
        // Load tenant info first
        const tenantResponse = await fetch("/api/v1/tenant", {
          method: "GET", credentials: "same-origin", cache: "no-store",
          referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
        });
        if (tenantResponse.status === 401) {
          if (mounted) window.location.replace("/login");
          return;
        }
        if (!tenantResponse.ok) throw new Error("Não foi possível identificar o salão.");
        await tenantResponse.json();
        if (!mounted) return;

        if (!bookingCode) {
          if (mounted) setError("Código de agendamento inválido. Use o link enviado pelo salão.");
          return;
        }

        // Try to load the appointment via the customer booking endpoint
        const apptResponse = await fetch(`/api/v1/appointments/customer/${bookingCode}`, {
          method: "GET", credentials: "same-origin", cache: "no-store",
          referrerPolicy: "no-referrer", headers: { Accept: "application/json" }
        });

        if (apptResponse.status === 404) {
          if (mounted) setError("Agendamento não encontrado ou link expirado.");
          return;
        }
        if (apptResponse.status === 403) {
          if (mounted) setError("Acesso negado. Este link é para um agendamento de outro salão.");
          return;
        }
        if (apptResponse.status === 401) {
          if (mounted) window.location.replace("/login");
          return;
        }
        if (!apptResponse.ok) throw new Error("Erro ao carregar os detalhes do agendamento.");

        const apptData = await apptResponse.json() as CustomerAppointmentResponse;
        if (!mounted) return;
        setAppointment(apptData.appointment);
        setCustomer(apptData.customer);
      } catch (cause) {
        if (mounted) setError(cause instanceof Error ? cause.message : "Falha ao carregar os detalhes.");
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void initialize();
    return () => { mounted = false; };
  }, [bookingCode]);

  const handleCancel = async () => {
    setError(null);
    setNotice(null);
    try {
      const response = await fetch(`/api/v1/appointments/customer/${bookingCode}/cancel`, {
        method: "POST", credentials: "same-origin", cache: "no-store",
        referrerPolicy: "no-referrer",
        headers: {
          "Accept": "application/json",
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ reason: cancelReason })
      });

      if (response.status === 401) { window.location.replace("/login"); return; }
      if (response.status === 403) {
        setError("Acesso negado. Este link é para um agendamento de outro salão.");
        return;
      }
      if (response.status === 404) {
        setError("Agendamento não encontrado.");
        return;
      }
      if (response.status === 409) {
        const data = await response.json() as { error?: string };
        throw new Error(data.error || "Cancelamento só é permitido com 24h de antecedência.");
      }
      if (!response.ok) {
        const data = await response.json() as { error?: string };
        throw new Error(data.error || "Não foi possível cancelar o agendamento.");
      }

      setNotice("Agendamento cancelado com sucesso!");
      setAppointment(null);
      setCustomer(null);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Falha ao cancelar o agendamento.");
    }
  };

  const now = new Date();
  const canCancel = appointment && new Date(appointment.startAtUtc) > now;

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

  if (loading) {
    return (
      <main className="workspace-shell">
        <div className="workspace-content">
          <div className="empty-state">Carregando os detalhes do agendamento…</div>
        </div>
      </main>
    );
  }

  if (error) {
    return (
      <main className="workspace-shell">
        <div className="workspace-content">
          <div className="empty-state">
            <strong>{error}</strong>
          </div>
        </div>
      </main>
    );
  }

  if (!appointment || !customer) {
    return (
      <main className="workspace-shell">
        <div className="workspace-content">
          <div className="empty-state">
            <strong>Nenhum agendamento encontrado</strong>
            <p>Este link pode ter expirado ou ser inválido.</p>
          </div>
        </div>
      </main>
    );
  }

  return (
    <main className="workspace-shell">
      <header className="workspace-header">
        <div className="brand-lockup">
          <span className="brand-mark" aria-hidden="true">a</span>
          <span className="brand-name">atendee<span>beauty</span></span>
        </div>
      </header>

      <section className="workspace-content">
        <div className="workspace-heading">
          <h1>Seu Agendamento</h1>
        </div>

        {error && <p className="feedback error" role="alert">{error}</p>}
        {notice && <p className="feedback success" role="status">{notice}</p>}

        <div className="appointment-card" style={{ maxWidth: "500px", margin: "0 auto" }}>
          <div className="appointment-card-top">
            <div>
              <h3>{appointment.service.name}</h3>
              <p className="appointment-time">
                {new Date(appointment.startAtUtc).toLocaleString("pt-BR", {
                  weekday: "long",
                  day: "numeric",
                  month: "long",
                  hour: "2-digit",
                  minute: "2-digit"
                })}
              </p>
            </div>
            <span className="status-badge" style={{ backgroundColor: STATUS_COLORS[appointment.appointmentStatus] }}>
              {STATUS_LABELS[appointment.appointmentStatus]}
            </span>
          </div>

          <div className="appointment-details">
            <div className="appointment-party">
              <p className="appointment-label"> Cliente</p>
              <p className="appointment-name">{customer.fullName}</p>
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

          {canCancel ? (
            <div className="appointment-actions">
              <button
                className="action-button danger"
                onClick={() => setShowCancelModal(true)}
                style={{ width: "100%" }}
              >
                Cancelar Agendamento
              </button>
            </div>
          ) : (
            <p style={{ color: "#96574b", fontSize: "0.9rem", marginTop: "1rem" }}>
              O prazo de 24h para cancelamento já expirou ou o agendamento não está mais ativo.
            </p>
          )}
        </div>
      </section>

      {showCancelModal && appointment && (
        <div className="dialog-backdrop" onClick={() => { setShowCancelModal(false); setCancelReason(""); }}>
          <dialog className="dialog-card" open>
            <h2>Cancelar Agendamento</h2>
            <p>Tem certeza que deseja cancelar este agendamento?</p>
            <p><strong>Cliente:</strong> {customer.fullName}</p>
            <p><strong>Serviço:</strong> {appointment.service.name}</p>
            <p><strong>Data/Hora:</strong> {new Date(appointment.startAtUtc).toLocaleString("pt-BR", {
              weekday: "short", day: "numeric", hour: "2-digit", minute: "2-digit"
            })}</p>
            <form onSubmit={(e) => { e.preventDefault(); handleCancel(); }}>
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
                <button type="button" className="secondary-button" onClick={() => { setShowCancelModal(false); setCancelReason(""); }}>
                  Voltar
                </button>
                <button type="submit" style={{ backgroundColor: "#c0392b", color: "white" }}>
                  Confirmar Cancelamento
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