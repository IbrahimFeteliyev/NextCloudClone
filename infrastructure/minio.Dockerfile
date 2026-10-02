FROM golang:1.24-alpine AS build
RUN apk add --no-cache git
ENV CGO_ENABLED=0
RUN --mount=type=cache,target=/go/pkg/mod --mount=type=cache,target=/root/.cache/go-build \
    go install github.com/minio/minio@RELEASE.2025-04-22T22-12-26Z && \
    cp /go/pkg/mod/github.com/minio/minio@*/LICENSE /LICENSE.minio

FROM alpine:3.21
RUN apk add --no-cache ca-certificates && \
    addgroup -g 1000 minio && adduser -D -u 1000 -G minio minio && \
    mkdir /data && chown minio:minio /data
COPY --from=build /go/bin/minio /usr/local/bin/minio
COPY --from=build /LICENSE.minio /usr/share/licenses/minio/LICENSE
USER minio
EXPOSE 9000 9001
ENTRYPOINT ["minio"]
CMD ["server", "/data", "--console-address", ":9001"]
