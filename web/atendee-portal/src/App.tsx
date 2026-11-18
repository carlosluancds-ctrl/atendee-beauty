import ActivationPage from "./ActivationPage";
import LoginPage from "./LoginPage";
import ServiceCatalogPage from "./ServiceCatalogPage";
import ProfessionalPage from "./ProfessionalPage";

type Props = { activationToken: string | null };

export default function App({ activationToken }: Props) {
  const path = window.location.pathname;
  const activationRoute = path === "/activate" || path.startsWith("/activate/");
  if (activationRoute || activationToken) return <ActivationPage activationToken={activationToken} />;
  if (path === "/team" || path.startsWith("/team/") || path === "/my-services") return <ProfessionalPage />;
  if (path === "/services" || path.startsWith("/services/")) return <ServiceCatalogPage />;
  return <LoginPage />;
}
