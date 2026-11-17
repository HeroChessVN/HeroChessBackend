import React, { useEffect, useRef, useState } from 'react';
import { api, defaultLineup, lineupPayload } from './api.js';

export default function LineupEditor({ token, catalog, lineups, onChanged, onError }) {
  const [editing, setEditing] = useState('new');
  const [name, setName] = useState('Đội hình mới');
  const [heroes, setHeroes] = useState({});
  const [skills, setSkills] = useState(['', '', '']);
  const [busy, setBusy] = useState(false);
  const current = lineups.find(item => item.id === editing);
  const presetFor = useRef(null);

  useEffect(() => {
    if (current) {
      presetFor.current = current.id;
      setName(current.name);
      setHeroes(Object.fromEntries(current.entries.map(item => [item.slotNo, item.heroId])));
      setSkills([1, 2, 3].map(slot => current.skills.find(item => item.slotNo === slot)?.skillId || ''));
      return;
    }
    // Đội hình mặc định: điền sẵn bộ quân hợp lệ để người chơi chỉ cần lưu.
    // Chỉ điền một lần cho mỗi lần mở "Tạo mới" để không ghi đè chỉnh sửa của người chơi.
    // Chờ catalog có dữ liệu rồi mới điền, nếu không sẽ điền rỗng.
    if (presetFor.current === 'new') return;
    if (!catalog?.slots?.length) return;
    presetFor.current = 'new';
    const preset = defaultLineup(catalog);
    setName('Đội hình mới');
    setHeroes(preset.heroes);
    setSkills(preset.skills);
  }, [editing, lineups, catalog]);

  const chosen = catalog.slots.map(slot => catalog.heroes.find(hero => hero.id === heroes[slot.slotNo]));

  const handleSlotChange = (slotNo, heroId) => {
    const slot = catalog.slots.find(s => s.slotNo === slotNo);
    if (slot?.classCode === 'SOLDIER') {
      const updated = { ...heroes };
      for (const s of catalog.slots) {
        if (s.classCode === 'SOLDIER') updated[s.slotNo] = heroId;
      }
      setHeroes(updated);
    } else {
      setHeroes({ ...heroes, [slotNo]: heroId });
    }
  };

  // Lấy danh sách hero đang dùng ở slot khác để không chọn trùng.
  const heroIdBySlot = {};
  for (const slot of catalog.slots) {
    if (heroes[slot.slotNo]) heroIdBySlot[slot.slotNo] = heroes[slot.slotNo];
  }

  const getAvailableHeroes = slot => {
    // Tốt dùng chung một hero cho cả năm slot nên không loại hero đang chọn khỏi danh sách.
    const peers = catalog.slots.filter(other =>
      other.slotNo !== slot.slotNo && other.classCode === slot.classCode
      && !(slot.classCode === 'SOLDIER' && other.classCode === 'SOLDIER'));
    const usedElsewhere = new Set(peers.map(other => heroIdBySlot[other.slotNo]).filter(Boolean));
    return catalog.heroes.filter(hero =>
      hero.classCode === slot.classCode && hero.isOwned && !usedElsewhere.has(hero.id));
  };

  const sp = chosen.reduce((sum, hero) => sum + (hero?.setupPoints || 0), 0);
  const repeated = chosen.filter(hero => hero?.characterId && chosen.filter(other => other?.characterId === hero.characterId).length > 1);
  const missing = chosen.filter(hero => !hero).length;
  const factionCount = factionId => new Set(chosen.filter(hero => hero?.classCode !== 'SOLDIER' &&
    hero?.factionIds?.includes(factionId)).map(hero => hero.id)).size;

  function fillOwned() {
    const preset = defaultLineup(catalog);
    setHeroes(preset.heroes);
    setSkills(preset.skills);
  }

  async function save() {
    // Chặn trước khi gọi API những lỗi server đã biết: thiếu hero, skill trùng/thiếu,
    // tên rỗng và nhân vật lịch sử bị lặp (server trả DUPLICATE_CHARACTER).
    if (missing || skills.some(value => !value) || new Set(skills).size !== 3 || !name.trim()) {
      onError(new Error('Cần đủ 16 hero, 3 skill khác nhau và tên đội hình.')); return;
    }
    if (repeated.length > 0) {
      onError(new Error('Một nhân vật lịch sử không được xuất hiện hai lần trong đội hình.')); return;
    }
    setBusy(true); onError(null);
    try {
      const payload = lineupPayload(catalog, name, heroes, skills, current?.revision);
      const saved = await api(token, current ? `/lineups/${current.id}` : '/lineups', { method: current ? 'PUT' : 'POST', body: payload });
      await onChanged(); setEditing(saved.id);
    } catch (error) { onError(error); }
    finally { setBusy(false); }
  }

  async function remove() {
    if (!current || !window.confirm(`Xóa đội hình “${current.name}”?`)) return;
    setBusy(true); onError(null);
    try { await api(token, `/lineups/${current.id}?expectedRevision=${current.revision}`, { method: 'DELETE' }); setEditing('new'); await onChanged(); }
    catch (error) { onError(error); }
    finally { setBusy(false); }
  }

  return <div className="editor-page"><div className="section-head"><div><span className="eyebrow">CHUẨN BỊ TRẬN ĐẤU</span><h1>Xếp đội hình</h1><p>Hero, trait, SP và skill lấy từ catalog của API.</p></div>
    <div className="budget"><strong>{sp} / {catalog.ruleset.setupBudget}</strong><span>Setup points</span></div></div>
    <div className="editor-layout"><aside className="card lineup-list"><h2>Đội hình đã lưu</h2><button className={editing === 'new' ? 'list-row active' : 'list-row'} onClick={() => setEditing('new')}>+ Tạo mới</button>
      {lineups.map(item => <button className={editing === item.id ? 'list-row active' : 'list-row'} key={item.id} onClick={() => setEditing(item.id)}><span>{item.name}</span><small>v{item.revision} · {item.totalSp}/{item.budget} SP</small></button>)}</aside>
      <div className="editor-main"><section className="card"><div className="form-head"><label>Tên đội hình<input maxLength={80} value={name} onChange={event => setName(event.target.value)} /></label><button className="secondary" onClick={fillOwned}>Về đội hình mặc định</button></div>
        <div className="form-status">{catalog.slots.length - missing}/{catalog.slots.length} vị trí · {sp > catalog.ruleset.setupBudget ? 'Vượt ngân sách SP' : 'Trong ngân sách'}{repeated.length > 0 ? ' · Có nhân vật lịch sử bị lặp' : ''}</div>
        <div className="slot-grid">{[...catalog.slots].sort((a, b) => a.slotNo - b.slotNo).map(slot => {
          const selected = catalog.heroes.find(hero => hero.id === heroes[slot.slotNo]);
          return <label className="slot" key={slot.slotNo}><span className="slot-title">#{slot.slotNo} · {slot.classCode} <small>({slot.startX},{slot.startY})</small></span>
            <select value={heroes[slot.slotNo] || ''} onChange={event => handleSlotChange(slot.slotNo, event.target.value)}><option value="">Chọn hero</option>
              {getAvailableHeroes(slot).map(hero => <option key={hero.id} value={hero.id}>{hero.name} · {hero.setupPoints} SP</option>)}</select>
            <small>{selected?.trait ? `${selected.trait.name}${selected.trait.description ? ` — ${selected.trait.description}` : ''}` : 'Chưa có trait'}</small></label>;
        })}</div></section>
        <section className="card"><h2>Command skill · 3 ô</h2><div className="skill-editor">{[0, 1, 2].map(index => {
          const selected = catalog.teamSkills.find(skill => skill.id === skills[index]);
          const otherFaction = skills.some((id, slot) => slot !== index && catalog.teamSkills.some(skill => skill.id === id && skill.factionId));
          return <label key={index}>Ô {index + 1}<select value={skills[index]} onChange={event => setSkills(skills.map((value, slot) => slot === index ? event.target.value : value))}><option value="">Chọn skill</option>
            {catalog.teamSkills.map(skill => {
              const minimum = skill.eligibility?.minNonSoldierFactionHeroes ?? 0;
              const members = skill.factionId ? factionCount(skill.factionId) : 0;
              return <option key={skill.id} value={skill.id} disabled={!!skill.factionId && (otherFaction || members < minimum)}>
                {skill.name}{minimum > 0 ? ` · ${members}/${minimum} cùng phe` : ''}
              </option>;
            })}</select>
            <small>{selected ? `${selected.implementationKey} · cooldown ${selected.cooldownTurns ?? 0} · lượt dùng ${selected.maxUses ?? '∞'}` : 'Chọn từ catalog'}</small></label>;
        })}</div><p className="muted">Server quyết định điều kiện phe và tính hợp lệ. Skill chưa có handler sẽ báo lỗi rõ trong trận.</p>
          <div className="actions"><button disabled={busy || sp > catalog.ruleset.setupBudget} onClick={save}>{current ? 'Lưu thay đổi' : 'Tạo đội hình'}</button>{current && <button disabled={busy} className="danger" onClick={remove}>Xóa</button>}</div></section></div></div>
  </div>;
}
