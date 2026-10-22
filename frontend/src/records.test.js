import { test } from 'node:test';
import assert from 'node:assert/strict';
import { durationLabel, eloClass, eloLabel, modeLabel, outcomeClass, outcomeLabel, query, rankBadge, recordLabel, statusLabel } from './records.js';

test('outcome labels cover win/loss/draw and never invent a value', () => {
  assert.equal(outcomeLabel('win'), 'Thắng');
  assert.equal(outcomeLabel('loss'), 'Thua');
  assert.equal(outcomeLabel('draw'), 'Hòa');
  assert.equal(outcomeLabel(null), '—');
  assert.equal(outcomeLabel('unknown'), '—');
  assert.equal(outcomeClass('win'), 'win');
  assert.equal(outcomeClass(null), '');
});

test('mode and status labels fall back to the raw value', () => {
  assert.equal(modeLabel('bot'), 'Bot');
  assert.equal(modeLabel('ranked'), 'PvP');
  assert.equal(modeLabel('custom'), 'custom');
  assert.equal(statusLabel('completed'), 'Đã kết thúc');
  assert.equal(statusLabel('selecting'), 'Chọn đội hình');
  assert.equal(statusLabel(null), '—');
});

test('duration renders minutes and seconds, or a dash when unknown', () => {
  assert.equal(durationLabel(45), '45s');
  assert.equal(durationLabel(125), '2 phút 5s');
  assert.equal(durationLabel(null), '—');
  assert.equal(durationLabel(0), '0s');
});

test('elo delta sign is preserved for both label and class', () => {
  assert.equal(eloLabel(12), '+12');
  assert.equal(eloLabel(-8), '-8');
  assert.equal(eloLabel(0), '0');
  assert.equal(eloLabel(null), '—');
  assert.equal(eloClass(12), 'up');
  assert.equal(eloClass(-8), 'down');
  assert.equal(eloClass(0), '');
});

test('rank badge shows medals for the top three and a number otherwise', () => {
  assert.equal(rankBadge(1), '🥇');
  assert.equal(rankBadge(3), '🥉');
  assert.equal(rankBadge(4), '#4');
  assert.equal(rankBadge(null), '—');
});

test('record summary lists wins, draws and losses', () => {
  assert.equal(recordLabel({ wins: 3, draws: 1, losses: 2 }), '3T · 1H · 2B');
});

test('query drops empty values so filters stay optional', () => {
  assert.equal(query({ limit: 20, mode: '', outcome: null }), '?limit=20');
  assert.equal(query({ cursor: 'abc' }), '?cursor=abc');
  assert.equal(query({}), '');
});
