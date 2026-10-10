export async function api(token, path, options = {}) {
  const headers = { ...options.headers, ...(token ? { Authorization: `Bearer ${token}` } : {}) };
  const body = options.body && typeof options.body !== 'string' ? JSON.stringify(options.body) : options.body;
  if (body) headers['Content-Type'] = 'application/json';
  const response = await fetch(`/api/v1${path}`, { ...options, headers, body });
  const data = response.status === 204 ? null : await response.json().catch(() => null);
  if (!response.ok) {
    const error = new Error(data?.message || data?.title || `${response.status} ${response.statusText}`);
    error.status = response.status;
    error.code = data?.code;
    error.details = data?.details || data?.errors;
    throw error;
  }
  return data;
}

export function lineupPayload(catalog, name, selections, skillIds, expectedRevision) {
  return {
    name: name.trim(), rulesetId: catalog.ruleset.id,
    entries: catalog.slots.map(slot => ({ slotNo: slot.slotNo, heroId: selections[slot.slotNo], cosmeticId: null })),
    skills: skillIds.map((skillId, index) => ({ slotNo: index + 1, skillId })),
    ...(expectedRevision == null ? {} : { expectedRevision })
  };
}

export function skillTarget(key, point, effectId, targetPieceId = null) {
  if (key === 'thanh' || key === 'rao') return point && { position: point };
  if (key === 'khien') return targetPieceId && { pieceId: targetPieceId };
  if (key === 'van_coc_tran_giang' || key === 'binh_lam_thuy_hien') return { paths: [4, 5, 6] };
  if (key === 'phan_ky_doat_the') return effectId && { effectId, ...(point ? { position: point } : {}) };
  if (key === 'pha_tran_doat_phong') return effectId && { effectId };
  return null;
}
