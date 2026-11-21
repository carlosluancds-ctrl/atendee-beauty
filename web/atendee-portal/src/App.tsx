import ActivationPage from "./ActivationPage";
import LoginPage from "./LoginPage";
import ServiceCatalogPage from "./ServiceCatalogPage";
import ProfessionalPage from "./ProfessionalPage";
import TenantMemberPage from "./TenantMemberPage";
import CustomerPage from "./CustomerPage";
import AppointmentPage from "./AppointmentPage";
import AppointmentFormPage from "./AppointmentFormPage";

type Props = { activationToken: string | null };

export default function App({ activationToken }: Props) {
  const path = window.location.pathname;
  const activationRoute = path === "/activate" || path.startsWith("/activate/");
  if (activationRoute || activationToken) return <ActivationPage activationToken={activationToken} />;
  if (path === "/tenant/members" || path.startsWith("/tenant/members/")) return <TenantMemberPage />;
  if (path === "/customers" || path.startsWith("/customers/")) return <CustomerPage />;
  if (path === "/appointments/new") return <AppointmentFormPage />;
  if (path === "/appointments") return <AppointmentPage />;
  if (path === "/team" || path.startsWith("/team/") || path === "/my-services") return <ProfessionalPage />;
  if (path === "/services" || path.startsWith("/services/")) return <ServiceCatalogPage />;
  return <LoginPage />;
}
