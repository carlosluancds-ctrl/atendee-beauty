import ActivationPage from "./ActivationPage";
import LoginPage from "./LoginPage";
import ServiceCatalogPage from "./ServiceCatalogPage";

type Props = { activationToken: string | null };

export default function App({ activationToken }: Props) {
  const path = window.location.pathname;
  const activationRoute = path === "/activate" || path.startsWith("/activate/");
  if (activationRoute || activationToken) return <ActivationPage activationToken={activationToken} />;
  if (path === "/services" || path.startsWith("/services/")) return <ServiceCatalogPage />;
  return <LoginPage />;
}
