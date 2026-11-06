#!/usr/bin/env bash
# Cascada de ramas: fusiona cada <origen> en su <destino>, en orden, y para en el primer fallo.
#
# Uso: cascada.sh <origen> <destino> [<origen> <destino> ...]
#
# Para cada par: si el destino ya contiene el origen, no hace nada; si no, fusiona, pasa los tests
# sobre la fusión y solo entonces sube el destino. Nunca reescribe historia ni fuerza un push. Si la
# fusión choca o sus tests fallan, no toca el destino: sube cascada/<origen>-a-<destino>, abre un PR
# hacia el destino y termina con error. Lo llama .github/workflows/cascada-ramas.yml.
set -euo pipefail

if [ $# -lt 2 ] || [ $(($# % 2)) -ne 0 ]; then
  echo "Uso: $0 <origen> <destino> [<origen> <destino> ...]" >&2
  exit 2
fi

git config user.name "github-actions[bot]"
git config user.email "41898282+github-actions[bot]@users.noreply.github.com"

# pedir_revision <origen> <destino> <motivo> <commit> <detalle>: sube <commit> a
# cascada/<origen>-a-<destino> y abre un PR hacia el destino si no hay uno abierto.
pedir_revision() {
  local from="$1" to="$2" motivo="$3" commit="$4" detalle="$5"
  local branch="cascada/${from//\//-}-a-${to//\//-}"
  # Sin --force: si la rama ya lleva una resolución a medias, el push se rechaza y se conserva.
  git push origin "$commit:refs/heads/$branch" || echo "$branch ya tiene cambios propios; no se toca."
  if [ -z "$(gh pr list --head "$branch" --base "$to" --state open --json number --jq '.[].number')" ]; then
    gh pr create --head "$branch" --base "$to" \
      --title "Cascada: fusionar $from en $to ($motivo)" \
      --body "$detalle Fusiona este PR cuando esté resuelto; la siguiente ejecución seguirá la cascada."
  fi
  echo "::error::Cascada $from → $to detenida ($motivo): PR abierto desde $branch."
}

# limpiar_revision <origen> <destino>: con la fusión ya en el destino, borra la rama
# cascada/<origen>-a-<destino> si queda alguna y no tiene un PR abierto. Una rama obsoleta
# haría que el siguiente fallo (que no fuerza el push) abriera el PR con contenido viejo.
limpiar_revision() {
  local from="$1" to="$2"
  local branch="cascada/${from//\//-}-a-${to//\//-}"
  git ls-remote --exit-code --heads origin "$branch" >/dev/null || return 0
  if [ -z "$(gh pr list --head "$branch" --state open --json number --jq '.[].number')" ]; then
    git push origin --delete "$branch"
    echo "Rama $branch borrada: ya no hace falta."
  fi
}

# cascada <origen> <destino>: 0 si el destino ya contiene el origen o se subió la fusión.
cascada() {
  local from="$1" to="$2"
  git fetch --quiet origin "$from" "$to"
  if git merge-base --is-ancestor "origin/$from" "origin/$to"; then
    echo "$to ya contiene $from."
    limpiar_revision "$from" "$to"
    return 0
  fi
  git checkout --quiet -B "$to" "origin/$to"
  if ! git merge --no-edit -m "Merge $from en $to (cascada automática)" "origin/$from"; then
    git merge --abort
    pedir_revision "$from" "$to" "conflictos" "origin/$from" \
      "La cascada automática no pudo fusionar \`$from\` en \`$to\` por conflictos. Resuélvelos en este PR."
    return 1
  fi
  if ! dotnet test Pelican4Ever.Tests -c Release; then
    pedir_revision "$from" "$to" "tests en rojo" "HEAD" \
      "La fusión de \`$from\` en \`$to\` no pasa los tests. Este PR ya lleva la fusión: corrígela aquí."
    return 1
  fi
  git push origin "$to"
  echo "$from fusionada en $to."
  limpiar_revision "$from" "$to"
}

while [ $# -ge 2 ]; do
  cascada "$1" "$2"
  shift 2
done
