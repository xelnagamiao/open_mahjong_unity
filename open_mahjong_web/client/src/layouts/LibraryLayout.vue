<template>
  <div class="library-shell">
    <router-view />
  </div>
</template>

<style scoped>
.library-shell {
  min-height: 100vh;
  isolation: isolate;
}
</style>
