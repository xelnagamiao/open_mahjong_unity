<script setup>
defineProps({name:String,size:{type:Number,default:20}})
const paths={
  grid:'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',
  chart:'M4 3v17h17 M8 15l4-5 4 2 5-7',
  person:'M16 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0 M4 21v-2a8 8 0 0 1 16 0v2',
  record:'M6 3h12v18H6z M9 7h6 M9 11h6 M9 15h4',
  fan:'M12 20 3 12 7 4h10l4 8-9 8 M7 4l5 16 5-16 M3 12h18',
  key:'M14 9a5 5 0 1 1-5-5 5 5 0 0 1 5 5 M12.5 12.5 21 21 M17 17l3-3',
  search:'M16 10a6 6 0 1 1-12 0 6 6 0 0 1 12 0 M15 15l6 6',
  arrow:'M4 12h16 M14 6l6 6-6 6',
  close:'M6 6l12 12 M6 18 18 6',
  refresh:'M20 7v5h-5 M4 17v-5h5 M19 8a8 8 0 0 0-14-2 M5 16a8 8 0 0 0 14 2',
  download:'M12 3v12 M7 10l5 5 5-5 M4 16v5h16v-5',
  external:'M14 3h7v7 M21 3 10 14 M10 3H3v18h18v-7',
  info:'M12 8h.01 M12 11v6 M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0',
  chevron:'M9 5l7 7-7 7',
  filter:'M3 5h18 M6 12h12 M9 19h6',
  copy:'M8 8h13v13H8z M16 8V3H3v13h5',
  expand:'M9 3H3v6 M15 3h6v6 M3 15v6h6 M21 15v6h-6',
  collapse:'M3 9h6V3 M21 9h-6V3 M3 15h6v6 M21 15h-6v6',
  check:'M4 12l5 5L20 6',
}
</script>
<template><svg :width="size" :height="size" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.65" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path :d="paths[name]||paths.chart"/></svg></template>
