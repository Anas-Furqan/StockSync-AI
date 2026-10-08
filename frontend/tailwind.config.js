/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,jsx}'],
  theme: {
    extend: {
      colors: {
        ink: '#07120f',
        panel: '#10241e',
        mint: '#5ee9b5',
      },
      boxShadow: {
        glow: '0 24px 80px rgba(20, 184, 128, 0.18)',
      },
    },
  },
  plugins: [],
}
