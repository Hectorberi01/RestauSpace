/** Transition d'entrée à chaque changement de page. */
export default function Template({children}: {children: React.ReactNode}) {
  return <div className="animate-page">{children}</div>;
}
