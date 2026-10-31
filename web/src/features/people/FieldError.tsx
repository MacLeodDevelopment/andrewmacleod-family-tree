/** The API's validation messages for one field, shown next to it. */
export function FieldError({ messages }: { messages: string[] | undefined }) {
  return messages?.map((message) => (
    <p key={message} role="alert" className="field-error">
      {message}
    </p>
  ))
}
