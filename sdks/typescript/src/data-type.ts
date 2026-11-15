import type { Post, PostData, PostDataInput, SavedPost } from './types.js'

// Checks an unknown value is a T: a zod schema's parse, a generated validator, or your own function. Throw if not.
export type Parse<T> = (value: unknown) => T

// One of your data types, typed. The API checks data against the type's JSON Schema; this makes the same promise in
// your code, so a weight reading can't be posted as anything else, and reading one back gives a WeightReading.
//
//   interface WeightReading { kg: number; note?: string }
//   const Weight = dataType<WeightReading>('weight-reading')
//   await member.posts.create({ tags: ['weight'], data: Weight.of({ kg: 82.4 }) })
//   const reading = Weight.read(post)   // WeightReading | undefined
export class DataType<T> {
  constructor(readonly key: string, private readonly parse?: Parse<T>) {}

  // The post's data, for posts.create and posts.edit
  of(value: T): PostDataInput<T> {
    return { type: this.key, value }
  }

  // Whether a post carries this type of data
  is(post: Pick<Post | SavedPost, 'data'>): post is Pick<Post, 'data'> & { data: PostData<T> } {
    return post.data?.type === this.key
  }

  // The post's data as a T, or undefined when it has none of this type. With a parser, the value is checked too.
  read(post: Pick<Post | SavedPost, 'data'>): T | undefined {
    if (!this.is(post)) return undefined
    return this.parse ? this.parse(post.data.value) : (post.data.value as T)
  }
}

export const dataType = <T>(key: string, parse?: Parse<T>) => new DataType<T>(key, parse)
