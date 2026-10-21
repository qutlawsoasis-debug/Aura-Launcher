import { defineConfig } from 'vite';
import { viteSingleFile } from 'vite-plugin-singlefile';
import { resolve } from 'path';
import fs from 'fs';

export default defineConfig(({ mode }) => {
  const isSingle = mode === 'single';

  return {
    base: './',
    root: __dirname,
    publicDir: 'public',
    plugins: isSingle ? [
      viteSingleFile(),
      {
        name: 'rename-standalone',
        closeBundle() {
          const outDir = resolve(__dirname, 'dist-single');
          const src = resolve(outDir, 'index.html');
          const dest = resolve(outDir, 'aura-v3-standalone.html');
          if (fs.existsSync(src)) {
            fs.renameSync(src, dest);
          }
        }
      }
    ] : [],
    build: {
      outDir: isSingle ? 'dist-single' : 'dist',
      emptyOutDir: true,
      rollupOptions: {
        input: {
          index: resolve(__dirname, 'index.html')
        }
      }
    },
    server: {
      port: 5173,
      strictPort: true
    },
    preview: {
      port: 4173,
      strictPort: false
    }
  };
});
