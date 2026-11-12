// Vai trò file: Kiểm thử các hàm định dạng và quyền — nơi dễ sai nhất khi bigint và phần trăm.
//
// Chạy: npm test (node --test src/lib/)

import assert from 'node:assert/strict';
import test from 'node:test';
import { formatCoin, formatPercent, initials } from './format.ts';
import { canWrite, hasPermission, parsePermissions } from './permissions.ts';

test('formatCoin giữ nguyên chữ số lớn và nhóm nghìn bằng dấu chấm', () => {
  assert.equal(formatCoin('0'), '0');
  assert.equal(formatCoin('1000'), '1.000');
  assert.equal(formatCoin('-250'), '-250');
  // Trên 2^53: ép Number sẽ mất chính xác.
  assert.equal(formatCoin('9007199254740993'), '9.007.199.254.740.993');
  assert.equal(formatCoin(''), '—');
  assert.equal(formatCoin(null), '—');
});

test('formatPercent dùng thang 0..100 của backend', () => {
  assert.equal(formatPercent(22.22), '22.2%');
  assert.equal(formatPercent(0), '0.0%');
  assert.equal(formatPercent(100), '100.0%');
  assert.equal(formatPercent(null), '—');
});

test('initials lấy chữ cái đầu và cuối', () => {
  assert.equal(initials('Bui Thi Xuan'), 'BX');
  assert.equal(initials('Quang'), 'QU');
  assert.equal(initials('  '), '?');
});

test('parsePermissions: null = chưa xác định → hiện đầy đủ, server mới quyết định', () => {
  assert.equal(hasPermission(parsePermissions(null), 'hero'), true);
  assert.equal(canWrite(parsePermissions(null), 'hero'), true);
});

test('parsePermissions: wildcard mở mọi miền', () => {
  const all = parsePermissions(['*']);
  assert.equal(hasPermission(all, 'hero'), true);
  assert.equal(canWrite(all, 'hero'), true);
});

test('parsePermissions: quyền "read" chỉ mở miền dashboard, không mở miền khác', () => {
  const limited = parsePermissions(['read']);
  assert.equal(hasPermission(limited, 'read'), true);
  assert.equal(hasPermission(limited, 'hero'), false);
  // Backend: key có trong danh sách là được ghi miền đó (HasPermissionAsync) — miền 'read' không có endpoint ghi.
  assert.equal(canWrite(limited, 'read'), true);
  assert.equal(canWrite(limited, 'hero'), false);
});

test('parsePermissions: không có quyền nào thì chặn hết miền', () => {
  const none = parsePermissions([]);
  assert.equal(hasPermission(none, 'hero'), false);
  assert.equal(canWrite(none, 'shop'), false);
});
