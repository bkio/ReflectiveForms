import { createReflectiveFormsApp } from './lib/createApp';

createReflectiveFormsApp({
  // Relative path → Vite proxy handles forwarding to the backend in dev.
  // In production, set VITE_API_BASE_URL to your actual API URL.
  apiBaseUrl: import.meta.env.VITE_API_BASE_URL || '/rf/api',
});
