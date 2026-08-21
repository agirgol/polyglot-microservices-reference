{{/*
  Pod-level settings every service here shares.

  enableServiceLinks is off for all of them. Kubernetes injects an environment
  variable per Service in the namespace — REDIS_PORT=tcp://10.96.0.1:6379 and so
  on — a convention from before cluster DNS. Spring reads spring.data.redis.port
  from REDIS_PORT and refuses to start when handed a URL. Nothing here discovers
  services that way.
*/}}
{{- define "polyglot.podDefaults" -}}
enableServiceLinks: false
{{- end -}}

{{- define "polyglot.labels" -}}
app.kubernetes.io/name: {{ .name }}
app.kubernetes.io/part-of: polyglot-reference
app.kubernetes.io/managed-by: {{ .root.Release.Service }}
{{- end -}}
