# syntax=docker/dockerfile:1

FROM node:22-alpine AS build
WORKDIR /app

COPY src/CoffeeShop.SignalRClient/package.json src/CoffeeShop.SignalRClient/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY src/CoffeeShop.SignalRClient/index.html src/CoffeeShop.SignalRClient/tsconfig.json ./
COPY src/CoffeeShop.SignalRClient/src/ ./src/
ARG VITE_HUB_URL=http://localhost:8080/message
ENV VITE_HUB_URL=$VITE_HUB_URL
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.29-alpine AS final
COPY --from=build --chown=101:101 /app/dist/ /usr/share/nginx/html/
USER 101
EXPOSE 8080
