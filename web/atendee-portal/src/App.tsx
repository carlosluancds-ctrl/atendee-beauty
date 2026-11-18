import ActivationPage from "./ActivationPage";
import LoginPage from "./LoginPage";

type Props = {
  activationToken: string | null;
};

export default function App({ activationToken }: Props) {
  const activationRoute =
    window.location.pathname === "/activate" ||
    window.location.pathname.startsWith("/activate/");

  if (activationRoute || activationToken) {
    return <ActivationPage activationToken={activationToken} />;
  }

  return <LoginPage />;
}