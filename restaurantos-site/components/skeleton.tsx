/** Blocs de chargement animés, affichés le temps qu'un contenu arrive. */
export function Skeleton({className = ''}: {className?: string}) {
  return <span aria-hidden="true" className={`skeleton block rounded-lg ${className}`} />;
}

export function FormSkeleton() {
  return (
    <div className="grid gap-4 sm:grid-cols-2" role="status" aria-label="Chargement du formulaire">
      {Array.from({length: 6}, (_, index) => (
        <div key={index} className="space-y-2">
          <Skeleton className="h-3 w-24" />
          <Skeleton className="h-10 w-full rounded-xl" />
        </div>
      ))}
      <div className="space-y-2 sm:col-span-2">
        <Skeleton className="h-3 w-20" />
        <Skeleton className="h-24 w-full rounded-xl" />
      </div>
      <Skeleton className="h-11 w-full rounded-xl sm:col-span-2" />
    </div>
  );
}
