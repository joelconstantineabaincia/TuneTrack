/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./Components/**/*.{razor,html,cshtml}",
    "./Pages/**/*.{razor,html,cshtml}",
    "./App.razor",
    "./**/*.razor"
  ],
  theme: {
    extend: {},
  },
  plugins: [],
}
