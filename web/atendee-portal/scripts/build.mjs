import { build } from "esbuild";
import { copyFile, mkdir, rm } from "node:fs/promises";

await rm("dist", { recursive: true, force: true });
await mkdir("dist", { recursive: true });
await build({
  entryPoints: ["src/main.tsx"],
  bundle: true,
  minify: true,
  target: ["es2022"],
  outdir: "dist/assets",
  entryNames: "app",
  assetNames: "[name]-[hash]",
  legalComments: "none"
});
await copyFile("index.html", "dist/index.html");