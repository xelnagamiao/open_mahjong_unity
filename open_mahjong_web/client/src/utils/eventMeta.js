/** 赛事状态 / 角色展示文案 */

export function eventStatusLabel(status) {
  return (
    {
      registered: '已注册',
      active: '已开启',
      closed: '已关闭',
    }[status] || status || '—'
  )
}

export function eventStatusTagType(status) {
  return (
    {
      registered: 'info',
      active: 'success',
      closed: 'danger',
    }[status] || 'info'
  )
}

export function eventRoleLabel(role, kind) {
  const noun = kind === 'base' ? '基地' : '赛事'
  return role === 'owner' ? `${noun}主管理员` : `${noun}子管理员`
}

export function parseVenueKind(kind) {
  return kind === 'base' ? 'base' : 'event'
}

export function venueKindLabel(kind) {
  return parseVenueKind(kind) === 'base' ? '基地' : '赛事'
}

export function venueKindTagType(kind) {
  return parseVenueKind(kind) === 'base' ? 'warning' : 'primary'
}

export function venueApplyHash(kind) {
  return parseVenueKind(kind) === 'base' ? '#sec-apply-base' : '#sec-apply-event'
}

export function venueManageHash(kind) {
  return parseVenueKind(kind) === 'base' ? '#sec-manage-base' : '#sec-manage-event'
}

export function venueAdminListPath(kind) {
  return parseVenueKind(kind) === 'base' ? '/admin/bases' : '/admin/events'
}

export function venueAdminDetailPath(kind, eventId) {
  const base = venueAdminListPath(kind)
  return eventId ? `${base}/${eventId}` : base
}

export function registrationStatusLabel(status) {
  return (
    {
      pending: '待审',
      approved: '已通过',
      rejected: '已拒绝',
      cancelled: '已取消',
    }[status] || status || '—'
  )
}

export function registrationStatusTagType(status) {
  return (
    {
      pending: 'warning',
      approved: 'success',
      rejected: 'danger',
      cancelled: 'info',
    }[status] || 'info'
  )
}
