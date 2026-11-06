// every iframe/frame element in this context, including those living inside shadow roots
return allScopes().flatMap(s => [...s.root.querySelectorAll('iframe,frame')]);
