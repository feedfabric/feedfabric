// Just enough of OpenAPI 3.1 to show the API's own document: operations grouped by tag, their parameters, bodies and
// responses, with $refs into components.schemas followed

export interface Schema {
  $ref?: string
  type?: string | string[]
  format?: string
  description?: string
  enum?: unknown[]
  items?: Schema
  properties?: Record<string, Schema>
  required?: string[]
  additionalProperties?: Schema | boolean
  oneOf?: Schema[]
  anyOf?: Schema[]
  allOf?: Schema[]
  minimum?: number
  maximum?: number
  minLength?: number
  maxLength?: number
  pattern?: string
  default?: unknown
}

interface MediaContent { schema?: Schema }

export interface Parameter { name: string; in: string; required?: boolean; description?: string; schema?: Schema }

export interface Operation {
  tags?: string[]
  summary?: string
  description?: string
  operationId?: string
  parameters?: Parameter[]
  requestBody?: { required?: boolean; content?: Record<string, MediaContent> }
  responses?: Record<string, { description?: string; content?: Record<string, MediaContent> }>
}

export interface OpenApiDocument {
  openapi: string
  info: { title: string; version: string; description?: string }
  paths: Record<string, Partial<Record<Method, Operation>>>
  components?: { schemas?: Record<string, Schema> }
}

export const Methods = ['get', 'post', 'put', 'patch', 'delete'] as const
export type Method = (typeof Methods)[number]

export interface Endpoint { method: Method; path: string; tag: string; operation: Operation }

// Tags are controller names; these read better as section titles
const TagNames: Record<string, string> = {
  UserFeed: 'Feeds and posts', AdminModeration: 'Admin: moderation', AdminPosts: 'Admin: posts', AdminUsers: 'Admin: users',
  AdminFeed: 'Admin: feeds', Admin: 'Admin', RankingProfiles: 'Ranking profiles', 'FeedFabric.Api': 'Other',
}
export const tagName = (tag: string) => TagNames[tag] ?? tag

// Every operation, in the document's order. The template's /weatherforecast is left out.
export function endpoints(doc: OpenApiDocument): Endpoint[] {
  return Object.entries(doc.paths).filter(([path]) => path !== '/weatherforecast').flatMap(([path, item]) =>
    Methods.filter(m => item[m]).map(method => ({ method, path, tag: item[method]!.tags?.[0] ?? 'Other', operation: item[method]! })))
}

export const refName = (ref: string) => ref.split('/').pop() ?? ref

export function resolve(doc: OpenApiDocument, schema: Schema): Schema {
  return schema.$ref ? doc.components?.schemas?.[refName(schema.$ref)] ?? {} : schema
}

// The JSON body's schema; ASP.NET lists text/plain and text/json too, with the same schema
export function jsonSchema(content?: Record<string, MediaContent>): Schema | undefined {
  if (!content) return undefined
  return (content['application/json'] ?? content['application/problem+json'] ?? Object.values(content)[0])?.schema
}

// A schema's type in a few characters: "string (uuid)", "HomeFeedPostResponse[]", "integer | null"
export function typeLabel(schema: Schema | undefined): string {
  if (!schema) return 'any'
  if (schema.$ref) return refName(schema.$ref)
  const variants = schema.oneOf ?? schema.anyOf
  if (variants) return variants.map(typeLabel).join(' | ')
  if (schema.allOf?.length === 1) return typeLabel(schema.allOf[0])
  const types = (Array.isArray(schema.type) ? schema.type : schema.type ? [schema.type] : [])
    // Numbers are allowed as strings too ("integer | string" with a digits pattern); that's an encoding detail
    .filter((t, _, all) => !(t === 'string' && (all.includes('integer') || all.includes('number')) && schema.pattern))
  const label = types.map(t => {
    if (t === 'array') return `${typeLabel(schema.items)}[]`
    if (t === 'object' && schema.additionalProperties && typeof schema.additionalProperties === 'object') {
      return `map of ${typeLabel(schema.additionalProperties)}`
    }
    return t
  }).join(' | ') || (schema.properties ? 'object' : schema.enum ? 'enum' : 'any')
  return schema.format ? `${label} (${schema.format})` : label
}

// What the schema adds besides its type: limits, allowed values, the default
export function constraints(schema: Schema | undefined): string[] {
  if (!schema) return []
  const notes: string[] = []
  if (schema.enum) notes.push(`one of ${schema.enum.map(v => JSON.stringify(v)).join(', ')}`)
  if (schema.minimum != null && schema.maximum != null) notes.push(`${schema.minimum}–${schema.maximum}`)
  else if (schema.minimum != null) notes.push(`≥ ${schema.minimum}`)
  else if (schema.maximum != null) notes.push(`≤ ${schema.maximum}`)
  if (schema.maxLength != null) notes.push(`up to ${schema.maxLength} characters`)
  if (schema.default !== undefined) notes.push(`default ${JSON.stringify(schema.default)}`)
  return notes
}

// The schema an object's fields come from, following a $ref, a single allOf, or the one non-null variant of a nullable
// reference (OpenAPI 3.1 writes those as oneOf [{ type: "null" }, { $ref }])
export function objectSchema(doc: OpenApiDocument, schema: Schema | undefined): Schema | undefined {
  if (!schema) return undefined
  const variants = (schema.oneOf ?? schema.anyOf)?.filter(v => v.type !== 'null')
  if (variants?.length === 1) return objectSchema(doc, variants[0])
  const resolved = resolve(doc, schema.allOf?.length === 1 ? schema.allOf[0] : schema)
  if (resolved.properties) return resolved
  if (resolved.items) return objectSchema(doc, resolved.items)
  return undefined
}
