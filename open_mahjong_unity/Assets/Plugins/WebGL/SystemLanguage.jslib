mergeInto(LibraryManager.library, {
  SalasasaSystemLanguage: function () {
    var parts = ((navigator.languages && navigator.languages[0]) || navigator.language || 'en').trim().replace(/_/g, '-').toLowerCase().split('-');
    if (parts[0] === 'fr') return 3;
    if (parts[0] === 'de') return 4;
    if (parts[0] === 'ru') return 5;
    if (parts[0] === 'ja') return 6;
    if (parts[0] !== 'zh') return 2;
    if (parts.indexOf('hans') >= 0) return 0;
    if (parts.indexOf('hant') >= 0) return 1;
    return parts.indexOf('tw') >= 0 || parts.indexOf('hk') >= 0 || parts.indexOf('mo') >= 0 ? 1 : 0;
  }
});
