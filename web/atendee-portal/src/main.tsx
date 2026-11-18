import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";
import "./styles.css";
import { extractActivationToken } from "./activation.mjs";

const activationToken = extractActivationToken(window.location.hash);

// The token is read from the fragment, then removed from the visible URL and
// browser history before React renders. Fragments are not sent in HTTP requests.
if (window.location.hash) {
  window.history.replaceState(
    null,
    document.title,
    window.location.pathname + window.location.search
  );
}

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <App activationToken={activationToken} />
  </React.StrictMode>
);