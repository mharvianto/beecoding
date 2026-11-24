import { defineConfig } from 'vitest/config';

// Unit tests for the plain-JS logic in src/lib (no components, no Vite plugins needed).
export default defineConfig({ test: { environment: 'jsdom', include: ['tests/**/*.test.js'] } });
