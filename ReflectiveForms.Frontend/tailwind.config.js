/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        primary: {
          DEFAULT: 'var(--rf-primary, #2563eb)',
          50: 'color-mix(in srgb, var(--rf-primary, #2563eb) 5%, white)',
          100: 'color-mix(in srgb, var(--rf-primary, #2563eb) 10%, white)',
          600: 'var(--rf-primary, #2563eb)',
          700: 'color-mix(in srgb, var(--rf-primary, #2563eb) 90%, black)',
        },
      },
    },
  },
  plugins: [
    require('@tailwindcss/typography'),
  ],
}
