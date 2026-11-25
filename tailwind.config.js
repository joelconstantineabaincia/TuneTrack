/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./Components/**/*.{razor,html,cshtml}",
    "./Pages/**/*.{razor,html,cshtml}",
    "./App.razor",
    "./**/*.razor"
  ],
  theme: {
    extend: {
      fontFamily: {
        display: ['Syne', 'ui-sans-serif', 'system-ui', '-apple-system', 'BlinkMacSystemFont', 'Segoe UI', 'sans-serif'],
        sans: ['Manrope', 'ui-sans-serif', 'system-ui', '-apple-system', 'BlinkMacSystemFont', 'Segoe UI', 'sans-serif'],
        mono: ['JetBrains Mono', 'ui-monospace', 'SFMono-Regular', 'Menlo', 'Monaco', 'Consolas', 'monospace']
      }
    },
  },
  plugins: [],
}
