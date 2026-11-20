/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ['./index.html', './src/**/*.{js,jsx}'],
  theme: {
    extend: {
      colors: {
        canvas: 'oklch(0.985 0.008 293)',
        surface: 'oklch(0.955 0.025 293)',
        ink: 'oklch(0.16 0.055 282)',
        flow: 'oklch(0.49 0.215 293)',
        signal: 'oklch(0.64 0.22 325)',
        voltage: 'oklch(0.84 0.095 250)',
      },
      fontFamily: {
        display: ['"Archivo"', 'sans-serif'],
        sans: ['"Source Sans 3"', 'sans-serif'],
        mono: ['"Azeret Mono"', 'monospace'],
      },
    },
  },
  plugins: [],
}
