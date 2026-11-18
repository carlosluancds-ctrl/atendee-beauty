import test from "node:test";
import assert from "node:assert/strict";
import { extractActivationToken, validateNewPassword } from "../src/activation.mjs";

const validToken = "A".repeat(43);

test("activation token is read only from a single well-formed URL fragment", () => {
  assert.equal(extractActivationToken(`#token=${validToken}`), validToken);
  assert.equal(extractActivationToken(`#source=email&token=${validToken}`), validToken);
  assert.equal(extractActivationToken(""), null);
  assert.equal(extractActivationToken("#token=bad"), null);
  assert.equal(extractActivationToken(`#token=${validToken}&token=${validToken}`), null);
  assert.equal(extractActivationToken(`#token=${"A".repeat(42)}!`), null);
});

test("password pre-validation mirrors the server policy", () => {
  assert.match(validateNewPassword("short") ?? "", /12 caracteres/);
  assert.match(validateNewPassword("aaaaaaaaaaaa") ?? "", /4 caracteres diferentes/);
  assert.equal(validateNewPassword("Correct horse battery staple 8"), null);
  assert.match(validateNewPassword("A".repeat(1025)) ?? "", /limite/);
});