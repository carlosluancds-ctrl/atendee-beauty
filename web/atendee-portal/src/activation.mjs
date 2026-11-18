const TOKEN_PATTERN = /^[A-Za-z0-9_-]{43}$/;

export function extractActivationToken(fragment) {
  const parameters = new URLSearchParams(
    fragment.startsWith("#") ? fragment.slice(1) : fragment
  );
  const tokens = parameters.getAll("token");

  if (tokens.length !== 1 || !TOKEN_PATTERN.test(tokens[0] ?? "")) {
    return null;
  }

  return tokens[0] ?? null;
}

export function validateNewPassword(password) {
  if (password.length > 1024) {
    return "A senha excede o limite permitido.";
  }

  if (password.length < 12) {
    return "Use uma senha com pelo menos 12 caracteres.";
  }

  if (new Set(Array.from(password)).size < 4) {
    return "Escolha uma senha com pelo menos 4 caracteres diferentes.";
  }

  return null;
}