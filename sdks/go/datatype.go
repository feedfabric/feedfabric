package feedfabric

import "encoding/json"

// DataType is one of your data types, typed. The API checks data against the type's JSON Schema; this makes the same
// promise in your code, so a weight reading can't be posted as anything else and reading one back gives your own type.
//
//	type WeightReading struct {
//		Kg   float64 `json:"kg"`
//		Note string  `json:"note,omitempty"`
//	}
//	var Weight = feedfabric.NewDataType[WeightReading]("weight-reading")
//
//	member.Posts.Create(ctx, feedfabric.PostInput{Tags: []string{"weight"}, Data: Weight.Of(WeightReading{Kg: 82.4})})
//	reading, ok, err := Weight.Read(post.Data)
type DataType[T any] struct {
	Key string
}

func NewDataType[T any](key string) DataType[T] {
	return DataType[T]{Key: key}
}

// Of is the post's data, for Posts.Create and Posts.Edit.
func (d DataType[T]) Of(value T) *PostDataInput {
	return &PostDataInput{Type: d.Key, Value: value}
}

// Is reports whether a post's data is of this type.
func (d DataType[T]) Is(data *PostData) bool {
	return data != nil && data.Type == d.Key
}

// Read decodes a post's data as a T. ok is false when the post has no data of this type; err is set when it has, but
// the value doesn't decode as a T.
func (d DataType[T]) Read(data *PostData) (value T, ok bool, err error) {
	if !d.Is(data) {
		return value, false, nil
	}
	err = json.Unmarshal(data.Value, &value)
	return value, err == nil, err
}
