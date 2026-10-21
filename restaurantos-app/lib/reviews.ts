import {api, del, post, put, type Page} from './api';

export type FeedbackStatus = 'Submitted' | 'Reviewed' | 'Archived';
export type SurveyStatus = 'Draft' | 'Published' | 'Closed';
export type QuestionType = 'Rating' | 'FreeText' | 'SingleChoice' | 'MultipleChoice';

export interface Feedback {
  id: string;
  locationId: string;
  customerId: string | null;
  orderId: string | null;
  rating: number;
  comment: string | null;
  status: FeedbackStatus;
  submittedAtUtc: string;
  reviewedBy: string | null;
  reviewedAtUtc: string | null;
  archivedAtUtc: string | null;
  isCustomerDetached: boolean;
  publishedAtUtc?: string | null;
}

export interface FeedbackSummary {
  count: number;
  averageRating: number | null;
  countByRating: {rating: number; count: number}[];
  withComment: number;
  submitted: number;
  reviewed: number;
  archived: number;
}

export interface Question {
  id: string;
  order: number;
  text: string;
  type: QuestionType;
  isRequired: boolean;
  choices: string[];
}

export interface Survey {
  id: string;
  title: string;
  status: SurveyStatus;
  publishedAtUtc: string | null;
  closedAtUtc: string | null;
  questions: Question[];
}

export interface FeedbackFilter {
  locationId?: string;
  status?: FeedbackStatus;
  includeArchived?: boolean;
  minRating?: number;
  maxRating?: number;
  from?: string;
  to?: string;
  hasComment?: boolean;
  published?: boolean;
}

export const feedbackStatusLabel: Record<FeedbackStatus, string> = {Submitted: 'À traiter', Reviewed: 'Traité', Archived: 'Archivé'};
export const surveyStatusLabel: Record<SurveyStatus, string> = {Draft: 'Brouillon', Published: 'Publié', Closed: 'Clos'};
export const questionTypeLabel: Record<QuestionType, string> = {
  Rating: 'Note de 1 à 5',
  FreeText: 'Texte libre',
  SingleChoice: 'Choix unique',
  MultipleChoice: 'Choix multiples',
};

const query = (filter: FeedbackFilter) => ({...filter});

export const listFeedbacks = (filter: FeedbackFilter, page: number, signal?: AbortSignal) =>
  api<Page<Feedback>>('/reviews/feedbacks', {query: {...query(filter), page, pageSize: 20}, signal});

export const feedbackSummary = (filter: FeedbackFilter, signal?: AbortSignal) =>
  api<FeedbackSummary>('/reviews/feedbacks/summary', {query: query(filter), signal});

export const markReviewed = (id: string) => post(`/reviews/feedbacks/${id}/reviewed`);

export const setFeedbackPublished = (id: string, published: boolean) =>
  post(`/reviews/feedbacks/${id}/${published ? 'publish' : 'unpublish'}`);

export const archiveFeedback = (id: string) => post(`/reviews/feedbacks/${id}/archive`);

export const submitFeedback = (body: {locationId: string; rating: number; comment: string | null; customerId: string | null; orderId: string | null}) =>
  post<{id: string}>('/reviews/feedbacks', body);

export const listSurveys = (signal?: AbortSignal) => api<Survey[]>('/reviews/surveys', {signal});

export const createSurvey = (title: string) => post<{id: string}>('/reviews/surveys', {title});

export const renameSurvey = (id: string, title: string) => put(`/reviews/surveys/${id}/title`, {title});

export interface QuestionInput {
  text: string;
  type: QuestionType;
  isRequired: boolean;
  choices: string[] | null;
}

export const addQuestion = (id: string, input: QuestionInput) => post(`/reviews/surveys/${id}/questions`, input);

export const changeQuestion = (id: string, questionId: string, input: QuestionInput) => put(`/reviews/surveys/${id}/questions/${questionId}`, input);

export const moveQuestion = (id: string, questionId: string, position: number) =>
  put(`/reviews/surveys/${id}/questions/${questionId}/position`, {position});

export const removeQuestion = (id: string, questionId: string) => del(`/reviews/surveys/${id}/questions/${questionId}`);

export const surveyAction = (id: string, action: 'publish' | 'close') => post(`/reviews/surveys/${id}/${action}`);

export interface SurveyQuestionResult {
  questionId: string;
  order: number;
  text: string;
  type: QuestionType;
  answered: number;
  average: number | null;
  ratings: number[] | null;
  choices: {choice: string; count: number}[] | null;
  texts: {text: string; submittedAtUtc: string}[] | null;
}

export interface SurveyResults {
  surveyId: string;
  title: string;
  status: SurveyStatus;
  responses: number;
  isSampled: boolean;
  questions: SurveyQuestionResult[];
}

export const surveyResults = (id: string, from: string | null, to: string | null, signal?: AbortSignal) =>
  api<SurveyResults>(`/reviews/surveys/${id}/results`, {query: {from: from ?? undefined, to: to ?? undefined}, signal});
