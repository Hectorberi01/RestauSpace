# syntax=docker/dockerfile:1.7
# Image de production du site public : build Next.js « standalone », exécution Node sans les dépendances de build.
# Les variables NEXT_PUBLIC_* sont figées AU BUILD (elles sont inscrites dans le code envoyé au navigateur).

FROM node:22-alpine AS deps
WORKDIR /app
RUN corepack enable
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN --mount=type=cache,id=pnpm,target=/root/.local/share/pnpm/store pnpm install --frozen-lockfile

FROM node:22-alpine AS build
WORKDIR /app
RUN corepack enable
ARG NEXT_PUBLIC_SITE_URL
ARG NEXT_PUBLIC_API_URL
ARG NEXT_PUBLIC_ADMIN_URL
ARG NEXT_PUBLIC_CONSOLE_URL
ARG NEXT_PUBLIC_SIGNUP_URL
ARG NEXT_PUBLIC_STOREFRONT_KEY
ARG NEXT_PUBLIC_CONTACT_EMAIL
ARG NEXT_PUBLIC_CONTACT_WHATSAPP
ARG NEXT_PUBLIC_CONTACT_PHONE_BJ
ARG NEXT_PUBLIC_CONTACT_PHONE_FR
ARG NEXT_PUBLIC_NEWSLETTER_URL
ARG NEXT_PUBLIC_SOCIAL_FACEBOOK
ARG NEXT_PUBLIC_SOCIAL_GITHUB
ARG NEXT_PUBLIC_SOCIAL_LINKEDIN
ARG NEXT_PUBLIC_SOCIAL_X
ARG NEXT_PUBLIC_SOCIAL_YOUTUBE
ENV NEXT_TELEMETRY_DISABLED=1 \
    NEXT_PUBLIC_SITE_URL=${NEXT_PUBLIC_SITE_URL} \
    NEXT_PUBLIC_API_URL=${NEXT_PUBLIC_API_URL} \
    NEXT_PUBLIC_ADMIN_URL=${NEXT_PUBLIC_ADMIN_URL} \
    NEXT_PUBLIC_CONSOLE_URL=${NEXT_PUBLIC_CONSOLE_URL} \
    NEXT_PUBLIC_SIGNUP_URL=${NEXT_PUBLIC_SIGNUP_URL} \
    NEXT_PUBLIC_STOREFRONT_KEY=${NEXT_PUBLIC_STOREFRONT_KEY} \
    NEXT_PUBLIC_CONTACT_EMAIL=${NEXT_PUBLIC_CONTACT_EMAIL} \
    NEXT_PUBLIC_CONTACT_WHATSAPP=${NEXT_PUBLIC_CONTACT_WHATSAPP} \
    NEXT_PUBLIC_CONTACT_PHONE_BJ=${NEXT_PUBLIC_CONTACT_PHONE_BJ} \
    NEXT_PUBLIC_CONTACT_PHONE_FR=${NEXT_PUBLIC_CONTACT_PHONE_FR} \
    NEXT_PUBLIC_NEWSLETTER_URL=${NEXT_PUBLIC_NEWSLETTER_URL} \
    NEXT_PUBLIC_SOCIAL_FACEBOOK=${NEXT_PUBLIC_SOCIAL_FACEBOOK} \
    NEXT_PUBLIC_SOCIAL_GITHUB=${NEXT_PUBLIC_SOCIAL_GITHUB} \
    NEXT_PUBLIC_SOCIAL_LINKEDIN=${NEXT_PUBLIC_SOCIAL_LINKEDIN} \
    NEXT_PUBLIC_SOCIAL_X=${NEXT_PUBLIC_SOCIAL_X} \
    NEXT_PUBLIC_SOCIAL_YOUTUBE=${NEXT_PUBLIC_SOCIAL_YOUTUBE}
COPY --from=deps /app/node_modules ./node_modules
COPY . .
RUN pnpm build

FROM node:22-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    HOSTNAME=0.0.0.0 \
    PORT=3000
RUN addgroup -S -g 1001 nodejs && adduser -S -u 1001 -G nodejs nextjs
COPY --from=build --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=build --chown=nextjs:nodejs /app/.next/static ./.next/static
COPY --from=build --chown=nextjs:nodejs /app/public ./public
COPY --from=build --chown=nextjs:nodejs /app/content ./content
USER nextjs
EXPOSE 3000
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:3000/ || exit 1
CMD ["node", "server.js"]
