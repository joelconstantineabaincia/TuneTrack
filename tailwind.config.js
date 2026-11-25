/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./src/TuneTrack/Components/**/*.{razor,html,cshtml}",
    "./src/TuneTrack/Pages/**/*.{razor,html,cshtml}",
    "./src/TuneTrack/App.razor",
    "./src/TuneTrack/**/*.razor"
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
