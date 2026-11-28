mergeInto(LibraryManager.library, {
  SalasasaSystemLanguage: function () {
    var candidates = [];
    if (typeof navigator !== 'undefined' && navigator) {
      if (navigator.languages && navigator.languages.length) {
        for (var i = 0; i < navigator.languages.length; i++) candidates.push(navigator.languages[i]);
      }
      if (navigator.language) candidates.push(navigator.language);
    }
    for (var j = 0; j < candidates.length; j++) {
      var raw = String(candidates[j] || '').trim().replace(/_/g, '-').toLowerCase();
      if (!raw) continue;
      var parts = raw.split('-');
      var primary = parts[0];
      if (primary === 'fr') return 3;
      if (primary === 'de') return 4;
      if (primary === 'ru') return 5;
      if (primary === 'ja') return 6;
      if (primary === 'es') return 7;
      if (primary === 'en') return 2;
      if (primary === 'zh') {
        if (parts.indexOf('hans') >= 0) return 0;
        if (parts.indexOf('hant') >= 0) return 1;
        return parts.indexOf('tw') >= 0 || parts.indexOf('hk') >= 0 || parts.indexOf('mo') >= 0 ? 1 : 0;
      }
    }
    return 2;
  }
});
