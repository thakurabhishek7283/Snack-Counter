# Build context: web/snack-counter-ui
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npx ng build --configuration production

# Static files plus a reverse proxy, so the browser talks to one origin and no CORS is needed.
FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/snack-counter-ui/browser /usr/share/nginx/html
EXPOSE 80
