-- Vai trò file: Tạo bảng app_user và index phục vụ ASP.NET Identity custom store.
CREATE SCHEMA IF NOT EXISTS identity;
REVOKE ALL ON SCHEMA identity FROM PUBLIC;

CREATE TABLE IF NOT EXISTS identity.app_user (
    id uuid PRIMARY KEY,
    user_name varchar(256),
    normalized_user_name varchar(256) UNIQUE,
    email varchar(256),
    normalized_email varchar(256) UNIQUE,
    password_hash text,
    security_stamp varchar(64) NOT NULL
);

REVOKE ALL ON identity.app_user FROM PUBLIC;
