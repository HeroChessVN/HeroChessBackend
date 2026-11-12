// Vai trò file: Skeleton tải. Dùng chung shape cho bảng và thẻ để giao diện không nhảy khi dữ liệu về.

interface SkeletonProps {
  className?: string;
}

export function Skeleton({ className = 'h-4 w-full' }: SkeletonProps) {
  return <div className={`skeleton ${className}`} aria-hidden />;
}

// TableSkeleton: Khung bảng gồm tiêu đề và N dòng.
export function TableSkeleton({ rows = 5, columns = 4 }: { rows?: number; columns?: number }) {
  return (
    <div className="p-4" role="status" aria-label="Đang tải dữ liệu">
      <div className="flex gap-4 border-b border-border pb-3">
        {Array.from({ length: columns }).map((_, index) => (
          <Skeleton key={index} className="h-3 flex-1" />
        ))}
      </div>
      <div className="flex flex-col gap-3 pt-3">
        {Array.from({ length: rows }).map((_, rowIndex) => (
          <div key={rowIndex} className="flex gap-4">
            {Array.from({ length: columns }).map((_, index) => (
              <Skeleton key={index} className="h-4 flex-1" />
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}

// CardSkeleton: Khung thẻ KPI.
export function CardSkeleton({ count = 4 }: { count?: number }) {
  return (
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4" role="status" aria-label="Đang tải dữ liệu">
      {Array.from({ length: count }).map((_, index) => (
        <div key={index} className="card p-5">
          <Skeleton className="h-3 w-24" />
          <Skeleton className="mt-3 h-8 w-32" />
          <Skeleton className="mt-3 h-3 w-20" />
        </div>
      ))}
    </div>
  );
}
