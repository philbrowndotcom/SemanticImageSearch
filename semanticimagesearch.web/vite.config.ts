import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-react';

// https://vitejs.dev/config/
export default defineConfig({
    plugins: [plugin()],
    server: {
        port: 54879,
        proxy: {
            '/api': {
                target: 'http://localhost:5256',
                changeOrigin: true,
            },
        },
    }
})
