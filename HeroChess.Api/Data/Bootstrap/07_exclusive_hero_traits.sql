-- Một bản định nghĩa trait chỉ gắn tối đa một hero; handler/implementation_key vẫn có thể dùng chung.
-- Nâng cấp dữ liệu cũ: giữ trait gốc cho hero có UUID nhỏ nhất, clone cho các hero còn lại.
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('hero_chess_exclusive_traits_v1'));
LOCK TABLE hero_chess.hero, hero_chess.hero_trait IN SHARE ROW EXCLUSIVE MODE;
DO $$
DECLARE shared record; new_trait_id uuid;
BEGIN
 FOR shared IN
   SELECT h.id AS hero_id,h.name AS hero_name,t.*
   FROM (SELECT id,trait_id,row_number() OVER (PARTITION BY trait_id ORDER BY id) AS ordinal
         FROM hero_chess.hero WHERE trait_id IS NOT NULL) ranked
   JOIN hero_chess.hero h ON h.id=ranked.id
   JOIN hero_chess.hero_trait t ON t.id=ranked.trait_id
   WHERE ranked.ordinal>1
 LOOP
   -- UUID ổn định theo hero để seed và migration dùng cùng ID; MD5 ở đây không dùng cho bảo mật.
   new_trait_id := md5('hero_chess.hero_trait:' || shared.hero_id::text)::uuid;
   IF EXISTS (SELECT 1 FROM hero_chess.hero_trait WHERE id=new_trait_id) THEN
     RAISE EXCEPTION 'Trait clone ID already exists for hero %. Resolve conflict before upgrading.', shared.hero_id;
   END IF;
   INSERT INTO hero_chess.hero_trait(id,code,name,kind,implementation_key,parameters,description)
   VALUES(new_trait_id,left(shared.code,40)||'-'||replace(shared.hero_id::text,'-',''),
          left(shared.name||' — '||shared.hero_name,120),shared.kind,shared.implementation_key,shared.parameters,shared.description);
   UPDATE hero_chess.hero SET trait_id=new_trait_id WHERE id=shared.hero_id;
 END LOOP;
END $$;
-- PostgreSQL vẫn cho nhiều NULL: hero cơ bản chưa có trait không bị ảnh hưởng.
CREATE UNIQUE INDEX IF NOT EXISTS uq_hero_trait_id ON hero_chess.hero(trait_id);
COMMENT ON COLUMN hero_chess.hero.trait_id IS 'Optional exclusive trait definition. Different heroes may reuse implementation_key, never the same trait row.';
COMMIT;
