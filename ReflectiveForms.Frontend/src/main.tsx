import { createReflectiveFormsApp } from './lib/createApp';

createReflectiveFormsApp({
  apiBaseUrl: import.meta.env.VITE_API_BASE_URL || 'http://localhost:9000/rf/api',
});
