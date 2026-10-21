export type Domain = 'users'|'restaurants'|'subscriptions'|'billing'|'plans'|'audit-logs';
export interface RecordItem {id:string;name:string;status:string;[key:string]:string|number|boolean|string[]}
export type AdminState = Record<Domain,RecordItem[]>;
export type Field = {key:string;label:string;type?:'text'|'number'|'email'|'select'|'date'|'textarea';options?:string[];required?:boolean;min?:number;step?:number};
