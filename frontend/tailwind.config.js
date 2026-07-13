/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        bg: "#0f1417",
        surface: "#171d20",
        surfaceMuted: "#1f272b",
        line: "#2a3338",
        ink: "#e6ebe9",
        muted: "#93a19c",
        accent: "#1fae98",
        accentMuted: "#12332e",
        warn: "#e2a336",
        warnMuted: "#33290f",
        danger: "#e5484d",
        dangerMuted: "#3a1516",
        success: "#2fbf71",
        successMuted: "#12321f"
      }
    }
  },
  plugins: []
};
