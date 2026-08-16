# Build context: frontend/ (docker-compose.yml, servicio `web`, context: ./frontend).

FROM node:20 AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

# `ng build` con el builder `application` (angular.json, Block 5) genera el output en
# dist/frontend/browser/, no directamente en dist/frontend/.
FROM nginx:alpine AS runtime

COPY --from=build /app/dist/frontend/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
