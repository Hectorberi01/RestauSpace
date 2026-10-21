// Preuves affichées sur l'accueil : À REMPLIR UNIQUEMENT AVEC DES DONNÉES RÉELLES.
// Chiffres vérifiables (nombre de restaurants actifs, commandes traitées...) et témoignages de clients qui ont donné leur
// accord écrit pour être cités. Tant que les deux listes sont vides, la section « Ils nous font confiance » est masquée.

export interface Stat {
  value: string;
  label: string;
}

export interface Testimonial {
  quote: string;
  name: string;
  role: string;
  place: string;
  /** Photo dans public/photos (ex. « temoignage-awa.webp »), avec l'accord de la personne. */
  photo?: string;
  /** Note donnée par le client, de 1 à 5. */
  rating?: number;
}

export const stats: Stat[] = [];

export const testimonials: Testimonial[] = [];
