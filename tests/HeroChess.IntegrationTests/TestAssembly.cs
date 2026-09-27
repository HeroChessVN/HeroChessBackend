// Vai trò file: Tắt chạy test song song vì các integration test dùng chung database và biến môi trường.
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
