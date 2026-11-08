const classes = { general: 'Tướng', advisor: 'Sĩ', elephant: 'Tượng', rook: 'Xe', cannon: 'Pháo', horse: 'Mã', soldier: 'Tốt' };
const sideName = side => side === 'red' ? 'Đỏ' : side === 'black' ? 'Đen' : 'Trận';
export const squareName = point => point ? `${String.fromCharCode(97 + point.x)}${point.y + 1}` : '?';

export function skillNotice(event, fallbackSide, fallbackSkill) {
  if (event?.type === 'team_skill.activated')
    return `${sideName(event.side || fallbackSide)}: dùng ${event.skillName || fallbackSkill || event.code || 'Command skill'}`;
  if (event?.type !== 'hero_skill.activated') return null;
  const hero = event.heroName || classes[event.pieceClass] || 'Tướng';
  const unit = event.heroName && classes[event.pieceClass] ? ` (${classes[event.pieceClass]})` : '';
  return `${sideName(event.side || fallbackSide)} · ${hero}${unit} dùng ${event.skillName || fallbackSkill || event.code || 'kỹ năng'}`;
}

export function actionLabel(entry, previous, catalog) {
  if (entry.kind === 'start') return 'Bàn cờ ban đầu · chưa đi nước nào';
  const actor = sideName(entry.actorSide);
  const events = Array.isArray(entry.resolvedEvents) ? entry.resolvedEvents : [];
  const hit = events.find(event => event.type === 'river_stake.hit');
  const hitLabel = hit && `Cọc của ${sideName(hit.side)} bắt ${hit.victimName || classes[hit.victimClass] || 'quân'} ${sideName(hit.victimSide)} tại ${squareName(hit.position)}`;
  if (entry.kind === 'move') {
    if (hitLabel) return hitLabel;
    const moved = events.find(event => event.type === 'piece.moved');
    const piece = previous?.stateAfter?.pieces?.find(item => item.pieceId === moved?.pieceId);
    const hero = catalog?.heroes?.find(item => item.id === piece?.heroId);
    const name = piece?.heroName || hero?.name || classes[piece?.class] || 'Quân';
    const from = squareName(piece?.position);
    const to = squareName(moved?.to || entry.stateAfter?.pieces?.find(item => item.pieceId === moved?.pieceId)?.position);
    const victim = previous?.stateAfter?.pieces?.find(item => item.position?.x === moved?.to?.x && item.position?.y === moved?.to?.y && item.side !== piece?.side);
    const captured = victim && entry.stateAfter?.pieces?.find(item => item.pieceId === victim.pieceId)?.status !== 'alive';
    return `${actor}: ${name} ${from} → ${to}${captured ? ' · ăn quân' : ''}`;
  }
  if (entry.kind === 'team_skill') {
    const skill = events.find(event => event.type === 'team_skill.activated');
    const frozen = previous?.stateAfter?.skillStates?.[entry.actorSide]?.find(item => item.slotNo === skill?.slot);
    const name = frozen?.name || catalog?.teamSkills?.find(item => item.implementationKey === skill?.code)?.name || skill?.code || 'Command skill';
    return skillNotice(skill, entry.actorSide, name) || `${actor}: dùng ${name}`;
  }
  if (entry.kind === 'hero_active') {
    const skill = events.find(event => event.type === 'hero_skill.activated');
    if (skill?.skillName) {
      const label = skillNotice(skill, entry.actorSide);
      return hitLabel ? `${label} · ${hitLabel}` : label;
    }
    if (skill?.code === 'quang_trung.special_move') {
      const moved = events.find(event => event.type === 'piece.moved');
      const before = previous?.stateAfter?.pieces?.find(piece => piece.pieceId === moved?.pieceId);
      return `${actor}: Quang Trung dùng kỹ năng ${squareName(before?.position)} → ${squareName(moved?.to)}`;
    }
    return `${actor}: dùng Bạch Đằng Giang`;
  }
  if (entry.kind === 'timeout') return `${actor}: hết 90 giây`;
  if (entry.kind === 'resign') return `${actor}: đầu hàng`;
  if (entry.kind === 'undo') return `${actor}: hoàn tác về bước ${events.find(event => event.type === 'match.undone')?.targetSequence ?? '?'}`;
  return `${actor}: ${entry.kind}`;
}
