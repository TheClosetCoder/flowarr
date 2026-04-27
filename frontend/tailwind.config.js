/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        ink: "#172026",
        panel: "#f7f8f4",
        line: "#d8ded6",
        accent: "#157a6e",
        warn: "#c77700",
        danger: "#b42318"
      }
    }
  },
  plugins: []
};
