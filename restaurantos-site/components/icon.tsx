import {
  BarChart3, Bell, Bike, Boxes, ChefHat, Code2, CreditCard, Gift, Globe, LayoutGrid, Megaphone, MessageSquareHeart, ReceiptText,
  Store, Tag, UsersRound, UtensilsCrossed, CalendarCheck, UserCog, type LucideIcon,
} from 'lucide-react';
import type {FeatureIcon} from '@/content/features';

const icons: Record<FeatureIcon, LucideIcon> = {
  orders: ReceiptText,
  kitchen: ChefHat,
  floor: LayoutGrid,
  reservations: CalendarCheck,
  menu: UtensilsCrossed,
  stock: Boxes,
  customers: UsersRound,
  loyalty: Gift,
  promotions: Tag,
  payments: CreditCard,
  notifications: Bell,
  reports: BarChart3,
  team: UserCog,
  locations: Store,
  integrations: Code2,
  reviews: MessageSquareHeart,
  marketing: Megaphone,
  online: Globe,
  delivery: Bike,
};

export function FeatureIconView({name, className = 'size-5'}: {name: FeatureIcon; className?: string}) {
  const Icon = icons[name];
  return <Icon className={className} aria-hidden="true" />;
}

/** Couleur de la pastille d'un module : fond et icône. */
const tones: Record<FeatureIcon, string> = {
  orders: 'bg-violet-50 text-violet-600',
  kitchen: 'bg-orange-50 text-orange-600',
  reservations: 'bg-sky-50 text-sky-600',
  menu: 'bg-emerald-50 text-emerald-600',
  floor: 'bg-rose-50 text-rose-600',
  stock: 'bg-indigo-50 text-indigo-600',
  team: 'bg-fuchsia-50 text-fuchsia-600',
  reports: 'bg-amber-50 text-amber-600',
  marketing: 'bg-pink-50 text-pink-600',
  customers: 'bg-cyan-50 text-cyan-600',
  locations: 'bg-orange-50 text-orange-700',
  integrations: 'bg-violet-50 text-violet-700',
  payments: 'bg-teal-50 text-teal-600',
  online: 'bg-blue-50 text-blue-600',
  delivery: 'bg-lime-50 text-lime-700',
  reviews: 'bg-yellow-50 text-yellow-700',
  loyalty: 'bg-pink-50 text-pink-600',
  promotions: 'bg-pink-50 text-pink-600',
  notifications: 'bg-sky-50 text-sky-600',
};

export function ModuleIcon({icon, size = 'size-11'}: {icon: FeatureIcon; size?: string}) {
  return (
    <span className={`grid ${size} shrink-0 place-items-center rounded-xl ${tones[icon]}`}>
      <FeatureIconView name={icon} />
    </span>
  );
}
