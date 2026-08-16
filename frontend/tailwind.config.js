/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ['./src/**/*.{html,ts}'],
  // Angular Material aplica sus propios estilos base; se desactiva el preflight
  // de Tailwind para que no pise los estilos de los componentes de Material.
  corePlugins: {
    preflight: false,
  },
  theme: {
    extend: {},
  },
  plugins: [],
}

